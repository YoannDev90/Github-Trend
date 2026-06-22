using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Github_Trend.Database;
using Github_Trend.Localization;
using Github_Trend.Utils;
using Serilog;

namespace Github_Trend.Services;

public sealed class GitHubAuthenticationService : IDisposable
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(5);
    private readonly GitHubDeviceFlowAuthService _deviceFlowService;
    private readonly GitHubAuthOptions _options;
    private readonly GitHubTokenProtector _protector;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly GitHubTokenRefreshService _tokenService;
    private readonly AppDatabase _db;

    private GitHubAuthTokenRecord? _currentRecord;

    public GitHubAuthenticationService()
        : this(new GitHubAuthOptions(), null) { }

    public GitHubAuthenticationService(GitHubAuthOptions options, AppDatabase? db = null)
    {
        _options = options;
        _protector = new GitHubTokenProtector();
        _tokenService = new GitHubTokenRefreshService(_options);
        _db = db ?? new AppDatabase();
        _deviceFlowService = new GitHubDeviceFlowAuthService(_options.ClientId, _options);
    }

    public GitHubAuthSession? CurrentSession { get; private set; }

    public bool IsConnected =>
        CurrentSession is not null
        && _currentRecord is not null
        && _currentRecord.RevokedAt is null;

    public string StatusText =>
        CurrentSession is null
            ? Localization.Localization.Instance.GetString(
                nameof(LocalizationService.GitHubAuthNotConnected)
            )
            : Localization.Localization.Instance.GetString(
                nameof(LocalizationService.GitHubAuthConnected),
                CurrentSession.Summary
            );

    public event EventHandler? SessionChanged;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await LoadCurrentSessionAsync();
    }

    public async Task<GitHubAuthSession?> BeginInteractiveSignInAsync(
        Action<string>? onProgress = null,
        Action<string>? onUserCodeAvailable = null
    )
    {
        await InitializeAsync();
        EnsureAuthConfigured();

        try
        {
            var deviceCodeResponse = await _deviceFlowService.RequestDeviceCodeAsync();

            if (
                string.IsNullOrWhiteSpace(deviceCodeResponse.UserCode)
                || string.IsNullOrWhiteSpace(deviceCodeResponse.VerificationUri)
            )
                throw new InvalidOperationException(
                    Localization.Localization.Instance.GetString(
                        nameof(LocalizationService.InvalidDeviceCodeResponse)
                    )
                );

            var verificationUrl =
                deviceCodeResponse.VerificationUriComplete ?? deviceCodeResponse.VerificationUri;
            if (string.IsNullOrWhiteSpace(verificationUrl))
                throw new InvalidOperationException(
                    Localization.Localization.Instance.GetString(
                        nameof(LocalizationService.MissingVerificationUrl)
                    )
                );

            var opened = OpenBrowser(new Uri(verificationUrl));
            onProgress?.Invoke(
                opened
                    ? Localization.Localization.Instance.GetString(
                        nameof(LocalizationService.GitHubAuthDeviceCodePromptOpen),
                        deviceCodeResponse.UserCode
                    )
                    : Localization.Localization.Instance.GetString(
                        nameof(LocalizationService.GitHubAuthDeviceCodePromptManual),
                        deviceCodeResponse.UserCode,
                        verificationUrl
                    )
            );
            onUserCodeAvailable?.Invoke(deviceCodeResponse.UserCode!);

            var (success, tokenResponse, error) = await _deviceFlowService.PollForTokenAsync(
                deviceCodeResponse.DeviceCode!,
                deviceCodeResponse.Interval,
                deviceCodeResponse.ExpiresIn
            );

            if (!success || tokenResponse?.AccessToken == null)
                throw new InvalidOperationException(
                    Localization.Localization.Instance.GetString(
                        nameof(LocalizationService.DeviceFlowAuthenticationFailed),
                        error ?? string.Empty
                    )
                );

            var profile = await _tokenService.FetchUserProfileAsync(tokenResponse.AccessToken);
            var localUserId = GetLocalUserId();

            var record = new GitHubAuthTokenRecord
            {
                UserId = localUserId,
                GitHubAccountId = profile.Id,
                AccessTokenEncrypted = _protector.Protect(tokenResponse.AccessToken),
                RefreshTokenEncrypted = null,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(8),
                RefreshTokenExpiresAt = null,
                ScopeList = tokenResponse.Scope?.Split(' ').ToList() ?? new List<string>(),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Login = profile.Login,
                Name = profile.Name,
                Email = profile.Email,
                AvatarUrl = profile.AvatarUrl,
            };

            await SaveRecordAsync(record);
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
        var record = await LoadCurrentRecordAsync();
        if (record is null || record.RevokedAt is not null)
        {
            _currentRecord = null;
            CurrentSession = null;
            RaiseSessionChanged();
            return null;
        }

        _currentRecord = record;
        if (
            record.IsExpired(RefreshSkew)
            && !string.IsNullOrWhiteSpace(record.RefreshTokenEncrypted)
        )
            try
            {
                await RefreshCurrentAsync();
                record = _currentRecord!;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to refresh token during session load");
            }

        CurrentSession = ToSession(record);
        RaiseSessionChanged();
        return CurrentSession;
    }

    public async Task<GitHubAuthSession?> RefreshCurrentAsync()
    {
        await _refreshLock.WaitAsync();
        try
        {
            var record = await LoadCurrentRecordAsync();
            if (record is null || record.RevokedAt is not null)
            {
                CurrentSession = null;
                _currentRecord = null;
                RaiseSessionChanged();
                return null;
            }

            if (string.IsNullOrWhiteSpace(record.RefreshTokenEncrypted))
                return ToSession(record);

            var refreshToken = _protector.Unprotect(record.RefreshTokenEncrypted);
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                await MarkDisconnectedAsync(record);
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
                await SaveRecordAsync(record);
                _currentRecord = record;
                CurrentSession = ToSession(record);
                RaiseSessionChanged();
                return CurrentSession;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Token refresh failed");
                await MarkDisconnectedAsync(record);
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
        var record = await LoadCurrentRecordAsync();
        if (record is null) return;

        record.RevokedAt = DateTimeOffset.UtcNow;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveRecordAsync(record);
        _currentRecord = null;
        CurrentSession = null;
        RaiseSessionChanged();
    }

    public async Task<string?> GetAccessTokenAsync(bool refreshIfNeeded = true)
    {
        var record = await LoadCurrentRecordAsync();
        if (record is null || record.RevokedAt is not null)
            return null;

        if (refreshIfNeeded && record.IsExpired(RefreshSkew))
        {
            var refreshed = await RefreshCurrentAsync();
            record = _currentRecord;
            if (refreshed is null || record is null)
                return null;
        }

        var accessToken = _protector.Unprotect(record.AccessTokenEncrypted);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            await MarkDisconnectedAsync(record);
            return null;
        }

        return accessToken;
    }

    public async Task<GitHubUserProfile?> GetCurrentUserProfileAsync(bool refreshIfNeeded = true)
    {
        var accessToken = await GetAccessTokenAsync(refreshIfNeeded);
        if (string.IsNullOrWhiteSpace(accessToken))
            return null;

        return await _tokenService.FetchUserProfileAsync(accessToken);
    }

    private async Task MarkDisconnectedAsync(GitHubAuthTokenRecord record)
    {
        record.RevokedAt = DateTimeOffset.UtcNow;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveRecordAsync(record);
        _currentRecord = null;
        CurrentSession = null;
        RaiseSessionChanged();
    }

    private void RaiseSessionChanged()
    {
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string GetLocalUserId()
    {
        return $"{Environment.UserName}@{Environment.MachineName}";
    }

    private static GitHubAuthSession ToSession(GitHubAuthTokenRecord record)
    {
        return new GitHubAuthSession(
            record.UserId,
            record.GitHubAccountId,
            record.Login ?? "github-user",
            record.Name,
            record.Email,
            record.AvatarUrl,
            record.ScopeList,
            record.ExpiresAt,
            record.RefreshTokenExpiresAt
        );
    }

    private void EnsureAuthConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId))
            throw new InvalidOperationException(
                Localization.Localization.Instance.GetString(
                    nameof(LocalizationService.GitHubClientIdNotConfigured)
                )
            );
    }

    private static bool OpenBrowser(Uri uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to open browser for {Url}", uri);
            return false;
        }
    }

    // --- Database helpers ---

    private async Task<GitHubAuthTokenRecord?> LoadCurrentRecordAsync()
    {
        try
        {
            var json = await _db.GetCurrentAuthTokenAsync();
            if (json is null) return null;

            var element = json.Value;
            return new GitHubAuthTokenRecord
            {
                UserId = element.GetProperty("user_id").GetString() ?? "",
                GitHubAccountId = element.GetProperty("github_account_id").GetInt64(),
                AccessTokenEncrypted = element.GetProperty("access_token_encrypted").GetString() ?? "",
                RefreshTokenEncrypted = element.TryGetProperty("refresh_token_expires_at", out var rt) && rt.ValueKind != System.Text.Json.JsonValueKind.Null
                    ? element.GetProperty("refresh_token_encrypted").GetString()
                    : null,
                ExpiresAt = DateTimeOffset.Parse(element.GetProperty("expires_at").GetString()!),
                RefreshTokenExpiresAt = element.TryGetProperty("refresh_token_expires_at", out var rte) && rte.ValueKind != System.Text.Json.JsonValueKind.Null
                    ? DateTimeOffset.Parse(rte.GetString()!)
                    : null,
                ScopeList = System.Text.Json.JsonSerializer.Deserialize<List<string>>(
                    element.GetProperty("scope_list_json").GetString()!
                ) ?? new List<string>(),
                CreatedAt = DateTimeOffset.Parse(element.GetProperty("created_at").GetString()!),
                UpdatedAt = DateTimeOffset.Parse(element.GetProperty("updated_at").GetString()!),
                RevokedAt = element.TryGetProperty("revoked_at", out var rev) && rev.ValueKind != System.Text.Json.JsonValueKind.Null
                    ? DateTimeOffset.Parse(rev.GetString()!)
                    : null,
                Login = element.TryGetProperty("login", out var login) && login.ValueKind != System.Text.Json.JsonValueKind.Null
                    ? login.GetString()
                    : null,
                Name = element.TryGetProperty("name", out var name) && name.ValueKind != System.Text.Json.JsonValueKind.Null
                    ? name.GetString()
                    : null,
                Email = element.TryGetProperty("email", out var email) && email.ValueKind != System.Text.Json.JsonValueKind.Null
                    ? email.GetString()
                    : null,
                AvatarUrl = element.TryGetProperty("avatar_url", out var avatar) && avatar.ValueKind != System.Text.Json.JsonValueKind.Null
                    ? avatar.GetString()
                    : null,
            };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load auth token from database");
            return null;
        }
    }

    private async Task SaveRecordAsync(GitHubAuthTokenRecord record)
    {
        try
        {
            await _db.UpsertAuthTokenAsync(
                record.UserId,
                record.GitHubAccountId,
                record.AccessTokenEncrypted,
                record.RefreshTokenEncrypted,
                record.ExpiresAt,
                record.RefreshTokenExpiresAt,
                record.ScopeList,
                record.CreatedAt,
                record.UpdatedAt,
                record.RevokedAt,
                record.Login,
                record.Name,
                record.Email,
                record.AvatarUrl
            );
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save auth token to database");
        }
    }

    public void Dispose()
    {
        _refreshLock.Dispose();
        _deviceFlowService.Dispose();
    }
}
