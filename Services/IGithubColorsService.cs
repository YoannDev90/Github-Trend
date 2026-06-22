using System.Threading.Tasks;

namespace Github_Trend.Services;

public interface IGithubColorsService
{
    Task<GithubColorsCatalog> FetchAsync(bool force = false);
}
