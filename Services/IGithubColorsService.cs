using System.Threading.Tasks;

namespace Github_Trend.Services;

public interface IGithubColorsService
{
    Task<GithubColorsCatalog> FetchAsync(bool force = false);
}

public sealed class GithubColorsServiceWrapper : IGithubColorsService
{
    public Task<GithubColorsCatalog> FetchAsync(bool force = false) =>
        GithubColorsService.FetchAsync(force);
}
