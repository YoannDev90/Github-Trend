using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace Github_Trend.Services;

public interface IRepositoryDetailsService
{
    Task<GithubTrendingRepository> EnrichAsync(
        GithubTrendingRepository repository,
        CancellationToken ct = default
    );

    Task<Bitmap?> TryLoadBitmapAsync(string? url, int? maxWidth = null, int? maxHeight = null);
}
