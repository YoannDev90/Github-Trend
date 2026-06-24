using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Github_Trend.Utils;
using Serilog;

namespace Github_Trend.Services;

public sealed class GitHubRateLimitService
{
    private readonly object _syncLock = new();
    private DateTimeOffset? _cooldownUntilUtc;
    private int _remaining = -1;

    public bool IsInCooldown
    {
        get
        {
            lock (_syncLock)
            {
                if (_cooldownUntilUtc is null) return false;
                if (DateTimeOffset.UtcNow >= _cooldownUntilUtc)
                {
                    _cooldownUntilUtc = null;
                    return false;
                }
                return true;
            }
        }
    }

    public TimeSpan CooldownRemaining
    {
        get
        {
            lock (_syncLock)
            {
                if (_cooldownUntilUtc is null) return TimeSpan.Zero;
                var remaining = _cooldownUntilUtc.Value - DateTimeOffset.UtcNow;
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }
    }

    public void TrackFromHeaders(HttpResponseMessage response)
    {
        if (
            response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining)
            && int.TryParse(System.Linq.Enumerable.FirstOrDefault(remaining), out var r)
        )
        {
            Interlocked.Exchange(ref _remaining, r);
        }
    }

    public void RegisterCooldown(HttpResponseMessage response)
    {
        var cooldown = ComputeCooldown(response);
        if (cooldown <= TimeSpan.Zero)
            cooldown = TimeSpan.FromSeconds(AppConfig.RateLimit.CooldownFallbackSeconds);

        lock (_syncLock)
        {
            var candidate = DateTimeOffset.UtcNow + cooldown;
            if (_cooldownUntilUtc is null || candidate > _cooldownUntilUtc.Value)
                _cooldownUntilUtc = candidate;
        }

        Log.Warning(
            "Rate-limit cooldown registered for {Seconds}s",
            (int)cooldown.TotalSeconds
        );
    }

    public TimeSpan ComputeRetryDelay(HttpResponseMessage response, int attempt) =>
        RetryHelper.ComputeDelay(response, attempt);

    public bool IsRetriableRateLimit(HttpResponseMessage response) =>
        RetryHelper.IsRetriableRateLimit(response);

    public async Task ApplyProactiveThrottleAsync(CancellationToken ct = default)
    {
        var remaining = Interlocked.CompareExchange(ref _remaining, -1, -1);
        if (remaining < 0) return;

        if (remaining <= AppConfig.RateLimit.CriticalThreshold)
        {
            Log.Warning("Rate-limit critical ({Remaining}), delaying 5s", remaining);
            await Task.Delay(5000, ct);
        }
        else if (remaining <= AppConfig.RateLimit.WarningThreshold)
        {
            Log.Debug("Rate-limit low ({Remaining}), delaying 1s", remaining);
            await Task.Delay(1000, ct);
        }
    }

    private TimeSpan ComputeCooldown(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is not null)
            return response.Headers.RetryAfter!.Delta!.Value;

        if (
            response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues)
            && long.TryParse(System.Linq.Enumerable.FirstOrDefault(resetValues), out var unixSeconds)
        )
        {
            var resetTime = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            return resetTime
                - DateTimeOffset.UtcNow
                + TimeSpan.FromSeconds(AppConfig.RateLimit.ResetSafetySeconds);
        }

        return TimeSpan.Zero;
    }
}
