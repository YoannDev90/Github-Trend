using System.Collections.Generic;
using System.Threading;

namespace Github_Trend.Services;

public interface IGithubTrendingService
{
    IAsyncEnumerable<GithubTrendingRepository> StreamAsync(
        bool force = false,
        string? since = null,
        string? language = null,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Thin wrapper around the static GithubTrendingService.</summary>
public sealed class GithubTrendingServiceWrapper : IGithubTrendingService
{
    public IAsyncEnumerable<GithubTrendingRepository> StreamAsync(
        bool force = false,
        string? since = null,
        string? language = null,
        CancellationToken cancellationToken = default
    ) => GithubTrendingService.StreamAsync(force, since, language, cancellationToken);
}
