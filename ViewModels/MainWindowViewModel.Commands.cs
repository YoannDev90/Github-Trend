using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Github_Trend.Localization;
using Github_Trend.Services.GraphQL;
using Github_Trend.Utils;
using Serilog;

namespace Github_Trend;

public sealed partial class MainWindowViewModel
{
    private void ExecuteOpenRepository(object? parameter)
    {
        if (
            parameter is not GithubTrendingRepository repo
            || string.IsNullOrWhiteSpace(repo.RepositoryLink)
        )
        {
            Log.Debug("OpenRepository: invalid parameter");
            return;
        }
        Log.Debug("OpenRepository: {Url}", repo.RepositoryLink);
        try
        {
            Process.Start(
                new ProcessStartInfo { FileName = repo.RepositoryLink, UseShellExecute = true }
            );
            Log.Information("Opened repository: {Url}", repo.RepositoryLink);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open repository: {Url}", repo.RepositoryLink);
            SetStatusMessageFromKey(
                nameof(LocalizationService.OpenRepositoryFailure),
                Array.Empty<object?>()
            );
        }
    }

    private async Task ExecuteStarRepositoryAsync(object? parameter)
    {
        if (
            parameter is not GithubTrendingRepository repo
            || string.IsNullOrWhiteSpace(repo.Repository)
        )
        {
            Log.Debug("StarRepository: invalid parameter");
            return;
        }
        Log.Debug("StarRepository: {Repo} starting", repo.Repository);

        if (!Auth.IsConnected)
        {
            Log.Debug("StarRepository: not authenticated, aborting");
            SetStatusMessageFromKey(
                nameof(LocalizationService.ConnectGitHubToStar),
                Array.Empty<object?>()
            );
            return;
        }

        try
        {
            var slug = RepositoryUrlParser.GetSlug(repo.Repository);
            if (!RepositoryUrlParser.TryParse(repo.Repository, out var owner, out var name))
            {
                Log.Warning("Cannot parse repository URL: {Repo}", repo.Repository);
                return;
            }

            Log.Debug("StarRepository: checking current star status for {Owner}/{Name}", owner, name);
            var isStarred = await _graphQlService.IsStarredAsync(owner, name) ?? false;

            if (isStarred)
            {
                if (ConfirmUnstarAsync is not null)
                {
                    var confirmed = await ConfirmUnstarAsync();
                    if (!confirmed) return;
                }

                await _graphQlService.ToggleStarAsync(owner, name, currentlyStarred: true);
                repo.IsStarred = false;
                RefreshRepoItem(repo);

                await _db.SetStarredAsync(slug, false);

                SetStatusMessageFromKey(
                    nameof(LocalizationService.UnstarRepositorySuccess),
                    new object?[] { repo.DisplayTitle }
                );
                Log.Information("Unstarred repository: {Slug}", slug);
            }
            else
            {
                await _graphQlService.ToggleStarAsync(owner, name, currentlyStarred: false);
                repo.IsStarred = true;
                RefreshRepoItem(repo);

                await _db.SetStarredAsync(slug, true);

                SetStatusMessageFromKey(
                    nameof(LocalizationService.StarRepositorySuccess),
                    new object?[] { repo.DisplayTitle }
                );
                Log.Information("Starred repository: {Slug}", slug);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to star/unstar repository: {Repo}", repo.Repository);
            SetStatusMessageFromKey(
                nameof(LocalizationService.StarRepositoryFailure),
                Array.Empty<object?>()
            );
        }
    }

    private async Task ExecuteWatchRepositoryAsync(object? parameter)
    {
        if (
            parameter is not GithubTrendingRepository repo
            || string.IsNullOrWhiteSpace(repo.Repository)
        )
        {
            Log.Debug("WatchRepository: invalid parameter");
            return;
        }
        Log.Debug("WatchRepository: {Repo} starting", repo.Repository);

        if (!Auth.IsConnected)
        {
            Log.Debug("WatchRepository: not authenticated, aborting");
            SetStatusMessageFromKey(
                nameof(LocalizationService.ConnectGitHubToWatch),
                Array.Empty<object?>()
            );
            return;
        }

        try
        {
            var slug = RepositoryUrlParser.GetSlug(repo.Repository);
            if (!RepositoryUrlParser.TryParse(repo.Repository, out var owner, out var name))
            {
                Log.Warning("Cannot parse repository URL: {Repo}", repo.Repository);
                return;
            }

            Log.Debug("WatchRepository: checking current watch status for {Slug}", slug);
            var isWatched = await _db.IsWatchedAsync(slug);

            if (isWatched)
            {
                if (ConfirmUnwatchAsync is not null)
                {
                    var confirmed = await ConfirmUnwatchAsync();
                    if (!confirmed) return;
                }

                await _graphQlService.ToggleWatchAsync(owner, name, currentlyWatched: true);
                repo.IsWatched = false;
                RefreshRepoItem(repo);
                await _db.SetWatchedAsync(slug, false);

                SetStatusMessageFromKey(
                    nameof(LocalizationService.UnwatchRepositorySuccess),
                    new object?[] { repo.DisplayTitle }
                );
                Log.Information("Unwatched repository: {Slug}", slug);
            }
            else
            {
                await _graphQlService.ToggleWatchAsync(owner, name, currentlyWatched: false);
                repo.IsWatched = true;
                RefreshRepoItem(repo);
                await _db.SetWatchedAsync(slug, true);

                SetStatusMessageFromKey(
                    nameof(LocalizationService.WatchRepositorySuccess),
                    new object?[] { repo.DisplayTitle }
                );
                Log.Information("Watched repository: {Slug}", slug);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to watch/unwatch repository: {Repo}", repo.Repository);
            SetStatusMessageFromKey(
                nameof(LocalizationService.WatchRepositoryFailure),
                Array.Empty<object?>()
            );
        }
    }

    private async Task ExecuteSaveRepositoryAsync(object? parameter)
    {
        if (
            parameter is not GithubTrendingRepository repo
            || string.IsNullOrWhiteSpace(repo.Repository)
        )
        {
            Log.Debug("SaveRepository: invalid parameter");
            return;
        }
        Log.Debug("SaveRepository: {Repo} starting", repo.Repository);

        if (!Auth.IsConnected)
        {
            Log.Debug("SaveRepository: not authenticated, aborting");
            SetStatusMessageFromKey(
                nameof(LocalizationService.ConnectGitHubToStar),
                Array.Empty<object?>()
            );
            return;
        }

        try
        {
            var slug = RepositoryUrlParser.GetSlug(repo.Repository);
            if (!RepositoryUrlParser.TryParse(repo.Repository, out var owner, out var name))
            {
                Log.Warning("Cannot parse repository URL: {Repo}", repo.Repository);
                return;
            }

            Log.Debug("SaveRepository: checking star status for {Owner}/{Name}", owner, name);
            var isStarred = await _graphQlService.IsStarredAsync(owner, name) ?? false;

            List<string>? selectedListIds = null;
            if (ShowSaveToStarListDialogAsync is not null)
            {
                Log.Debug("SaveRepository: fetching star lists");
                var lists = await _graphQlService.GetStarListsAsync();
                selectedListIds = await ShowSaveToStarListDialogAsync(
                    lists,
                    async name => await _graphQlService.CreateStarListAsync(name, null, false)
                );
            }

            if (selectedListIds is null)
            {
                Log.Debug("SaveRepository: cancelled by user");
                return;
            }

            if (!isStarred)
            {
                await _graphQlService.ToggleStarAsync(owner, name, currentlyStarred: false);
                repo.IsStarred = true;
                RefreshRepoItem(repo);
                await _db.SetStarredAsync(slug, true);
            }

            var nodeId = await _graphQlService.GetRepositoryNodeIdAsync(owner, name, CancellationToken.None);
            if (nodeId is not null)
            {
                var existingIds = await _graphQlService.GetItemListMembershipsAsync(nodeId);
                var mergedIds = existingIds.Union(selectedListIds).ToList();
                await _graphQlService.AddRepositoryToStarListsAsync(nodeId, mergedIds);
                Log.Information("Added {Slug} to {Count} star list(s)", slug, selectedListIds.Count);
            }

            SetStatusMessageFromKey(
                nameof(LocalizationService.SaveRepositorySuccess),
                new object?[] { repo.DisplayTitle }
            );
            Log.Information("Saved repository: {Slug}", slug);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save repository: {Repo}", repo.Repository);
            SetStatusMessageFromKey(
                nameof(LocalizationService.SaveRepositoryFailure),
                Array.Empty<object?>()
            );
        }
    }

    private async Task ExecuteDismissRepositoryAsync(object? parameter)
    {
        if (
            parameter is not GithubTrendingRepository repo
            || string.IsNullOrWhiteSpace(repo.Repository)
        )
        {
            Log.Debug("DismissRepository: invalid parameter");
            return;
        }
        Log.Debug("DismissRepository: {Repo} starting", repo.Repository);

        try
        {
            var slug = RepositoryUrlParser.GetSlug(repo.Repository);
            Log.Debug("DismissRepository: dismissing {Slug}", slug);
            await _db.SetDismissedAsync(slug, true);
            repo.IsDismissed = true;

            TrendingRepositories.Remove(repo);
            repo.Dispose();

            OnPropertyChanged(nameof(TrendingCount));
            OnPropertyChanged(nameof(TrendingLabel));
            OnPropertyChanged(nameof(ShowTrendingContent));
            OnPropertyChanged(nameof(ShowTrendingEmpty));

            SetStatusMessageFromKey(
                nameof(LocalizationService.DismissRepositorySuccess),
                new object?[] { repo.DisplayTitle }
            );
            Log.Information("Dismissed repository: {Slug}", slug);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to dismiss repository: {Repo}", repo.Repository);
            SetStatusMessageFromKey(
                nameof(LocalizationService.DismissRepositoryFailure),
                Array.Empty<object?>()
            );
        }
    }

    private async Task ExecuteShowDismissedAsync()
    {
        Log.Debug("ShowDismissed: starting");
        try
        {
            var dismissedSlugs = await _db.GetDismissedSlugsAsync();
            if (dismissedSlugs.Count == 0)
            {
                Log.Debug("ShowDismissed: no dismissed repos");
                SetStatusMessageFromKey(
                    nameof(LocalizationService.NoDismissedRepositories),
                    Array.Empty<object?>()
                );
                return;
            }

            SetStatusMessageFromKey(
                nameof(LocalizationService.DismissedCount),
                new object?[] { dismissedSlugs.Count }
            );
            Log.Information("Dismissed repos: {Count}", dismissedSlugs.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load dismissed repos");
        }
    }

    private async Task ExecuteUndismissAllAsync()
    {
        Log.Debug("UndismissAll: starting");
        try
        {
            var dismissedSlugs = await _db.GetDismissedSlugsAsync();
            Log.Debug("UndismissAll: clearing {Count} dismissed repos", dismissedSlugs.Count);
            foreach (var slug in dismissedSlugs)
                await _db.SetDismissedAsync(slug, false);

            SetStatusMessageFromKey(
                nameof(LocalizationService.AllDismissedCleared),
                new object?[] { dismissedSlugs.Count }
            );
            Log.Information("Undismissed all repos: {Count}", dismissedSlugs.Count);

            _ = RefreshTrendingRepositoriesAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to undismiss all repos");
        }
    }

    private async Task<HashSet<string>> FetchWatchedSlugsFromRestAsync(CancellationToken ct)
    {
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            slugs = await _apiClient.GetWatchedRepositorySlugsAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to fetch watched repos from REST");
        }
        return slugs;
    }
}
