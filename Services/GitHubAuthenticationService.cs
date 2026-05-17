using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Github_Trend;

public sealed class GitHubAuthenticationService
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(5);
    private readonly GitHubAuthOptions _options;
    private readonly GitHubTokenProtector _protector;
    private readonly GitHubTokenRefreshService _tokenService;
    private readonly GitHubAuthTokenStore _tokenStore;
    private readonly GitHubDeviceFlowAuthService _deviceFlowService;
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
        _deviceFlowService = new GitHubDeviceFlowAuthService(_options.ClientId, _options);
    }

    public event EventHandler? SessionChanged;

    public GitHubAuthSession? CurrentSession { get; private set; }

    public bool IsConnected => CurrentSession is not null && _currentRecord is not null && _currentRecord.RevokedAt is null;

    public string StatusText => CurrentSession is null
        ? "Non connecté à GitHub"
        : $"Connecté à GitHub: {CurrentSession.Summary}";

    public async Task InitializeAsync()
    {
        await LoadCurrentSessionAsync();
    }

    public async Task<GitHubAuthSession?> BeginInteractiveSignInAsync(Action<string>? onProgress = null)
    {
        await InitializeAsync();
        EnsureAuthConfigured();

        try
        {
            // Request device code from GitHub
            var deviceCodeResponse = await _deviceFlowService.RequestDeviceCodeAsync();
            
            if (string.IsNullOrWhiteSpace(deviceCodeResponse.UserCode) || 
                string.IsNullOrWhiteSpace(deviceCodeResponse.VerificationUri))
            {
                throw new InvalidOperationException("Invalid device code response from GitHub");
            }

            var verificationUrl = deviceCodeResponse.VerificationUriComplete ?? deviceCodeResponse.VerificationUri;
            if (string.IsNullOrWhiteSpace(verificationUrl))
            {
                throw new InvalidOperationException("Missing GitHub verification URL");
            }

            var opened = OpenBrowser(new Uri(verificationUrl));
            onProgress?.Invoke(opened
                ? $"Code GitHub: {deviceCodeResponse.UserCode}. Valide dans le navigateur ouvert."
                : $"Code GitHub: {deviceCodeResponse.UserCode}. Ouvre: {verificationUrl}");

            // Poll for token with timeout
            var (success, tokenResponse, error) = await _deviceFlowService.PollForTokenAsync(
                deviceCodeResponse.DeviceCode!,
                deviceCodeResponse.Interval,
                deviceCodeResponse.ExpiresIn
            );

            if (!success || tokenResponse?.AccessToken == null)
            {
                throw new InvalidOperationException($"Device flow authentication failed: {error}");
            }

            // Fetch user profile and create session
            var profile = await _tokenService.FetchUserProfileAsync(tokenResponse.AccessToken);
            var localUserId = GetLocalUserId();

            var record = new GitHubAuthTokenRecord
            {
                UserId = localUserId,
                GitHubAccountId = profile.Id,
                AccessTokenEncrypted = _protector.Protect(tokenResponse.AccessToken),
                RefreshTokenEncrypted = null,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(8), // Device flow tokens typically last 8 hours
                RefreshTokenExpiresAt = null,
                ScopeList = tokenResponse.Scope?.Split(' ').ToList() ?? new List<string>(),
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
            RaiseSessionChanged();
            return CurrentSession;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "GitHub device-flow authentication failed");
            throw;
        }
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



    private async Task MarkDisconnectedAsync(GitHubAuthTokenRecord record, string reason)
    {
        record.RevokedAt = DateTimeOffset.UtcNow;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await _tokenStore.UpsertAsync(record);
        _currentRecord = null;
        CurrentSession = null;
        RaiseSessionChanged();
    }

    private void RaiseSessionChanged() => SessionChanged?.Invoke(this, EventArgs.Empty);

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

    private void EnsureAuthConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId))
        {
            throw new InvalidOperationException("GitHub App client id is not configured. Set GITHUB_APP_CLIENT_ID environment variable.");
        }
    }

    private static bool OpenBrowser(Uri uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri.ToString())
            {
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            // best-effort open in default browser
            return false;
        }
    }
}


