using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Github_Trend;

public sealed class GitHubAuthenticationService
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(5);
    private readonly GitHubAuthOptions _options;
    private readonly GitHubTokenProtector _protector;
    private readonly GitHubTokenRefreshService _tokenService;
    private readonly GitHubAuthTokenStore _tokenStore;
    private readonly GitHubLoopbackAuthServer _server;
    private readonly ConcurrentDictionary<string, PendingFlow> _flowsById = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PendingFlow> _flowsByState = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private GitHubAuthTokenRecord? _currentRecord;

    public GitHubAuthenticationService()
        : this(new GitHubAuthOptions())
    {
    }

    public GitHubAuthenticationService(GitHubAuthOptions options)
    {
        _options = options;
        _protector = new GitHubTokenProtector();
        _tokenService = new GitHubTokenRefreshService(_options);
        _tokenStore = new GitHubAuthTokenStore();
        _server = new GitHubLoopbackAuthServer(_options, this);
    }

    public event EventHandler? SessionChanged;

    public GitHubAuthSession? CurrentSession { get; private set; }

    public bool IsConnected => CurrentSession is not null && _currentRecord is not null && _currentRecord.RevokedAt is null;

    public string StatusText => CurrentSession is null
        ? "Non connecté à GitHub"
        : $"Connecté à GitHub: {CurrentSession.Summary}";

    public async Task InitializeAsync()
    {
        await _server.StartAsync();
        await LoadCurrentSessionAsync();
    }

    public async Task<GitHubAuthSession?> BeginInteractiveSignInAsync()
    {
        await InitializeAsync();
        EnsureAuthConfigured();

        var pending = CreatePendingFlow();
        var startUrl = new Uri(new Uri(_options.LocalBaseUrl), $"/auth/github/start?flowId={Uri.EscapeDataString(pending.FlowId)}");

        OpenBrowser(startUrl);

        var session = await pending.Completion.Task;
        return session;
    }

    public async Task<GitHubAuthSession?> LoadCurrentSessionAsync()
    {
        var record = await _tokenStore.GetCurrentAsync();
        if (record is null || record.RevokedAt is not null)
        {
            _currentRecord = null;
            CurrentSession = null;
            RaiseSessionChanged();
            return null;
        }

        _currentRecord = record;
         if (record.IsExpired(RefreshSkew) && !string.IsNullOrWhiteSpace(record.RefreshTokenEncrypted))
         {
             try
             {
                 await RefreshCurrentAsync();
                 record = _currentRecord!;
             }
             catch
             {
                 // keep the stored session visible and let the caller trigger re-auth if needed
             }
         }

        CurrentSession = record is null ? null : ToSession(record);
        RaiseSessionChanged();
        return CurrentSession;
    }

    public async Task<GitHubAuthSession?> RefreshCurrentAsync()
    {
        await _refreshLock.WaitAsync();
        try
        {
            var record = await _tokenStore.GetCurrentAsync();
            if (record is null || record.RevokedAt is not null)
            {
                CurrentSession = null;
                _currentRecord = null;
                RaiseSessionChanged();
                return null;
            }

            if (string.IsNullOrWhiteSpace(record.RefreshTokenEncrypted))
            {
                return ToSession(record);
            }

            var refreshToken = _protector.Unprotect(record.RefreshTokenEncrypted);
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                await MarkDisconnectedAsync(record, "refresh token unavailable");
                return null;
            }

            try
            {
                EnsureAuthConfigured();
                var refreshed = await _tokenService.RefreshAsync(refreshToken);
                record.AccessTokenEncrypted = _protector.Protect(refreshed.AccessToken);
                record.RefreshTokenEncrypted = string.IsNullOrWhiteSpace(refreshed.RefreshToken)
                    ? record.RefreshTokenEncrypted
                    : _protector.Protect(refreshed.RefreshToken);
                record.ExpiresAt = refreshed.ExpiresAt;
                record.RefreshTokenExpiresAt = refreshed.RefreshTokenExpiresAt;
                record.ScopeList = refreshed.ScopeList.ToList();
                record.UpdatedAt = DateTimeOffset.UtcNow;
                await _tokenStore.UpsertAsync(record);
                _currentRecord = record;
                CurrentSession = ToSession(record);
                RaiseSessionChanged();
                return CurrentSession;
            }
            catch
            {
                await MarkDisconnectedAsync(record, "refresh failed");
                return null;
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task SignOutAsync()
    {
        var record = await _tokenStore.GetCurrentAsync();
        if (record is null)
        {
            return;
        }

        record.RevokedAt = DateTimeOffset.UtcNow;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await _tokenStore.UpsertAsync(record);
        _currentRecord = null;
        CurrentSession = null;
        RaiseSessionChanged();
    }

    public async Task<string?> GetAccessTokenAsync(bool refreshIfNeeded = true)
    {
        var record = await _tokenStore.GetCurrentAsync();
        if (record is null || record.RevokedAt is not null)
        {
            return null;
        }

        if (refreshIfNeeded && record.IsExpired(RefreshSkew))
        {
            var refreshed = await RefreshCurrentAsync();
            record = _currentRecord;
            if (refreshed is null || record is null)
            {
                return null;
            }
        }

        var accessToken = _protector.Unprotect(record.AccessTokenEncrypted);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            await MarkDisconnectedAsync(record, "access token unavailable");
            return null;
        }

        return accessToken;
    }

    public async Task<GitHubUserProfile?> GetCurrentUserProfileAsync(bool refreshIfNeeded = true)
    {
        var accessToken = await GetAccessTokenAsync(refreshIfNeeded);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        return await _tokenService.FetchUserProfileAsync(accessToken);
    }

    internal async Task<string> BuildAuthorizationUrlAsync(string? flowId)
    {
        await InitializeAsync();
        EnsureAuthConfigured();

        var pending = !string.IsNullOrWhiteSpace(flowId) && _flowsById.TryGetValue(flowId, out var existing)
            ? existing
            : CreatePendingFlow();

        if (!string.IsNullOrWhiteSpace(flowId) && pending.FlowId != flowId)
        {
            _flowsById[flowId] = pending;
        }

        var state = RandomToken();
        pending.State = state;
        _flowsByState[state] = pending;

        var authorizeUrl = new StringBuilder("https://github.com/login/oauth/authorize");
        authorizeUrl.Append("?client_id=").Append(Uri.EscapeDataString(_options.ClientId));
        authorizeUrl.Append("&redirect_uri=").Append(Uri.EscapeDataString(_options.CallbackUrl));
        authorizeUrl.Append("&state=").Append(Uri.EscapeDataString(state));
        authorizeUrl.Append("&scope=").Append(Uri.EscapeDataString(_options.Scope));
        authorizeUrl.Append("&allow_signup=false");
        return authorizeUrl.ToString();
    }

    internal async Task<GitHubAuthSession> CompleteAuthorizationAsync(string state, string code)
    {
        if (!_flowsByState.TryRemove(state, out var pending))
        {
            throw new InvalidOperationException("Invalid or expired authorization state.");
        }

        pending.Completed = true;

        var exchange = await _tokenService.ExchangeCodeAsync(code, state);
        var profile = await _tokenService.FetchUserProfileAsync(exchange.AccessToken);
        var localUserId = GetLocalUserId();

        var record = new GitHubAuthTokenRecord
        {
            UserId = localUserId,
            GitHubAccountId = profile.Id,
            AccessTokenEncrypted = _protector.Protect(exchange.AccessToken),
            RefreshTokenEncrypted = string.IsNullOrWhiteSpace(exchange.RefreshToken) ? null : _protector.Protect(exchange.RefreshToken),
            ExpiresAt = exchange.ExpiresAt,
            RefreshTokenExpiresAt = exchange.RefreshTokenExpiresAt,
            ScopeList = exchange.ScopeList.ToList(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Login = profile.Login,
            Name = profile.Name,
            Email = profile.Email,
            AvatarUrl = profile.AvatarUrl
        };

        await _tokenStore.UpsertAsync(record);
        _currentRecord = record;
        CurrentSession = ToSession(record);
        pending.Completion.TrySetResult(CurrentSession);
        RaiseSessionChanged();
        return CurrentSession;
    }

    internal async Task<string> HandleStartAsync(string? flowId)
    {
        await InitializeAsync();
        var authorizeUrl = await BuildAuthorizationUrlAsync(flowId);
        return authorizeUrl;
    }

    internal async Task<(bool Success, string Message)> HandleRefreshRequestAsync()
    {
        var refreshed = await RefreshCurrentAsync();
        return refreshed is null
            ? (false, "Refresh failed or no active session.")
            : (true, $"Refreshed session for {refreshed.Summary}");
    }

    internal void FailPendingFlow(string state, string message)
    {
        if (_flowsByState.TryRemove(state, out var pending))
        {
            pending.Completed = true;
            pending.Completion.TrySetException(new InvalidOperationException(message));
        }
    }

    internal async Task RejectPendingFlowAsync(string? flowId, string message)
    {
        if (string.IsNullOrWhiteSpace(flowId))
        {
            return;
        }

        if (_flowsById.TryRemove(flowId, out var pending))
        {
            pending.Completed = true;
            pending.Completion.TrySetException(new InvalidOperationException(message));
        }

        await Task.CompletedTask;
    }

    private async Task MarkDisconnectedAsync(GitHubAuthTokenRecord record, string reason)
    {
        record.RevokedAt = DateTimeOffset.UtcNow;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await _tokenStore.UpsertAsync(record);
        _currentRecord = null;
        CurrentSession = null;
        RaiseSessionChanged();
    }

    private PendingFlow CreatePendingFlow()
    {
        var pending = new PendingFlow(RandomToken());
        _flowsById[pending.FlowId] = pending;
        return pending;
    }

    private static string RandomToken()
        => Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .Replace("=", string.Empty, StringComparison.Ordinal)
            .Replace("+", string.Empty, StringComparison.Ordinal)
            .Replace("/", string.Empty, StringComparison.Ordinal);

    private static string GetLocalUserId()
        => $"{Environment.UserName}@{Environment.MachineName}";

    private static GitHubAuthSession ToSession(GitHubAuthTokenRecord record)
        => new(
            record.UserId,
            record.GitHubAccountId,
            record.Login ?? "github-user",
            record.Name,
            record.Email,
            record.AvatarUrl,
            record.ScopeList,
            record.ExpiresAt,
            record.RefreshTokenExpiresAt);

    private void RaiseSessionChanged() => SessionChanged?.Invoke(this, EventArgs.Empty);

    private void EnsureAuthConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            throw new InvalidOperationException("GitHub App client id/secret are not configured. Set GITHUB_APP_CLIENT_ID and GITHUB_APP_CLIENT_SECRET.");
        }
    }

    private static void OpenBrowser(Uri uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri.ToString())
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // best-effort open in default browser
        }
    }

    private sealed class PendingFlow
    {
        public PendingFlow(string flowId)
        {
            FlowId = flowId;
            Completion = new TaskCompletionSource<GitHubAuthSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public string FlowId { get; }

        public string? State { get; set; }

        public bool Completed { get; set; }

        public TaskCompletionSource<GitHubAuthSession> Completion { get; }
    }
}


