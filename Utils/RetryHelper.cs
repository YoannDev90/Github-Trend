using System;
using System.Linq;
using System.Net.Http;
using Serilog;

namespace Github_Trend.Utils;

public static class RetryHelper
{
    public static TimeSpan ComputeDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is not null)
            return response.Headers.RetryAfter!.Delta!.Value
                + TimeSpan.FromMilliseconds(
                    Random.Shared.Next(
                        Constants.RateLimit.RetryJitterMinMilliseconds,
                        Constants.RateLimit.RetryJitterMaxMilliseconds
                    )
                );

        if (
            response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues)
            && long.TryParse(resetValues.FirstOrDefault(), out var unixSeconds)
        )
        {
            var resetTime = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            var delay =
                resetTime
                - DateTimeOffset.UtcNow
                + TimeSpan.FromSeconds(Constants.RateLimit.ResetSafetySeconds);
            if (delay > TimeSpan.Zero)
                return delay
                    + TimeSpan.FromMilliseconds(
                        Random.Shared.Next(
                            Constants.RateLimit.RetryJitterMinMilliseconds,
                            Constants.RateLimit.RetryJitterMaxMilliseconds
                        )
                    );
        }

        var exponential = Constants.RateLimit.BaseBackoffMilliseconds * Math.Pow(2, attempt);
        var bounded = Math.Min(exponential, Constants.RateLimit.MaxBackoffMilliseconds);
        return TimeSpan.FromMilliseconds(bounded + Random.Shared.Next(50, 200));
    }

    public static bool IsRetriableRateLimit(HttpResponseMessage response)
    {
        if (response.StatusCode == (System.Net.HttpStatusCode)429)
            return true;

        return response.StatusCode == System.Net.HttpStatusCode.Forbidden
            && (
                HasRateLimitRemainingZero(response)
                || response.Headers.RetryAfter is not null
            );
    }

    private static bool HasRateLimitRemainingZero(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining)
            && string.Equals(
                remaining.FirstOrDefault(),
                "0",
                StringComparison.OrdinalIgnoreCase
            );
    }
}
