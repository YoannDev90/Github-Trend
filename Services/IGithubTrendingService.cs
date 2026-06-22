using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Github_Trend.Services;

public interface IGithubTrendingService
{
    Task<List<GithubTrendingRepository>> FetchAsync(
        bool force = false,
        string? since = null,
        string? language = null
    );

    IAsyncEnumerable<GithubTrendingRepository> StreamAsync(
        bool force = false,
        string? since = null,
        string? language = null,
        CancellationToken cancellationToken = default
    );
}
