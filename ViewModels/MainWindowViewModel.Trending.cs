using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Github_Trend.Localization;
using Github_Trend.Utils;
using Serilog;

namespace Github_Trend;

public sealed partial class MainWindowViewModel
{
    private async Task RefreshTrendingRepositoriesAsync()
    {
        var previousCts = _trendingCts;
        var cts = new CancellationTokenSource();
        _trendingCts = cts;
        previousCts?.Cancel();
        previousCts?.Dispose();

        IsTrendingLoading = true;

        try
        {
            var sinceValues = GetSinceValues();
            var languages = Filter.SelectedLanguageFilters;

            Log.Information(
                "Trending refresh start since=[{Since}] languages=[{Languages}]",
                string.Join(',', sinceValues),
                string.Join(',', languages.Select(x => x ?? "<all>"))
            );

            foreach (var repo in TrendingRepositories)
                repo.Dispose();
            TrendingRepositories.Clear();
            OnPropertyChanged(nameof(ShowTrendingContent));
            var seen = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            );
            OnPropertyChanged(nameof(TrendingCount));
            OnPropertyChanged(nameof(TrendingLabel));

            var token = cts.Token;

            var streams = sinceValues
                .SelectMany(since =>
                    languages.Select(language =>
                        _trendingService.StreamAsync(false, since, language, token)
                    )
                )
                .ToList();

            var channel = Channel.CreateBounded<GithubTrendingRepository>(
                new BoundedChannelOptions(20)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    AllowSynchronousContinuations = false,
                }
            );

            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        await Task.WhenAll(
                            streams.Select(async stream =>
                            {
                                await foreach (var repo in stream.WithCancellation(token))
                                    await channel.Writer.WriteAsync(repo, token);
                            })
                        );
                    }
                    catch (OperationCanceledException)
                    {
                        Log.Debug("Trending refresh cancelled");
                    }
                    finally
                    {
                        channel.Writer.TryComplete();
                    }
                },
                token
            );

            var starredSlugs = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            );
            var watchedSlugs = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            );
            var dismissedSlugs = await _db.GetDismissedSlugsAsync();

            if (Auth.IsConnected)
            {
                try
                {
                    starredSlugs = await _db.GetStarredSlugsAsync();
                    watchedSlugs = await _db.GetWatchedSlugsAsync();

                    var freshStarred = await _graphQlService.GetAllStarredSlugsAsync(token);
                    if (freshStarred.Count > 0)
                    {
                        starredSlugs = freshStarred;
                        await _db.SetStarredSlugsAsync(freshStarred);
                    }

                    var freshWatched = await FetchWatchedSlugsFromRestAsync(token);
                    if (freshWatched.Count > 0)
                    {
                        watchedSlugs = freshWatched;
                        await _db.SetWatchedSlugsAsync(freshWatched);
                    }

                    Log.Information(
                        "Loaded {StarCount} starred and {WatchCount} watched repos",
                        starredSlugs.Count,
                        watchedSlugs.Count
                    );
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to fetch starred/watched repos");
                }
            }

            var batch = new List<GithubTrendingRepository>(
                Constants.Trending.MaxParallelEnrichmentRequests * 5
            );
            await foreach (var repo in channel.Reader.ReadAllAsync(token))
            {
                var key =
                    !string.IsNullOrWhiteSpace(repo.Repository) ? repo.Repository!
                    : !string.IsNullOrWhiteSpace(repo.Name) ? repo.Name!
                    : null;

                if (key != null && !seen.Add(key))
                    continue;

                var slug = RepositoryUrlParser.GetSlug(repo.Repository);
                if (!string.IsNullOrWhiteSpace(slug) && dismissedSlugs.Contains(slug))
                    continue;

                var visual = ApplyLanguageBrush(repo);
                if (!string.IsNullOrWhiteSpace(slug))
                {
                    visual.IsStarred = starredSlugs.Contains(slug);
                    visual.IsWatched = watchedSlugs.Contains(slug);
                    visual.IsDismissed = dismissedSlugs.Contains(slug);
                }
                batch.Add(visual);

                if (batch.Count >= 10)
                {
                    FlushBatch(batch, TrendingRepositories);
                }
            }

            if (batch.Count > 0)
                FlushBatch(batch, TrendingRepositories);

            Log.Information("Trending stream complete: {Count}", TrendingRepositories.Count);
        }
        catch (OperationCanceledException)
        {
            Log.Debug("Trending refresh superseded by newer request");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Trending loading failed");
        }
        finally
        {
            IsTrendingLoading = false;
            OnPropertyChanged(nameof(ShowTrendingContent));
            OnPropertyChanged(nameof(ShowTrendingEmpty));
            if (ReferenceEquals(_trendingCts, cts))
            {
                cts.Dispose();
                _trendingCts = null;
            }
        }
    }

    private async Task RefreshColorsAsync()
    {
        Log.Debug("RefreshColors: starting");
        if (_isInitializing || _isRefreshing)
            return;
        try
        {
            _isRefreshing = true;
            SetStatusMessageFromKey(
                nameof(LocalizationService.StatusColorsRefreshing),
                Array.Empty<object?>()
            );
            (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();

            var catalog = await _colorsService.FetchAsync(force: true);
            await Filter.RefreshColorsAsync(catalog);

            if (TrendingRepositories.Count > 0)
            {
                var refreshed = TrendingRepositories.Select(ApplyLanguageBrush).ToList();
                TrendingRepositories.Clear();
                foreach (var repo in refreshed)
                    TrendingRepositories.Add(repo);
            }

            SetStatusMessageFromKey(
                nameof(LocalizationService.StatusColorsRefreshed),
                new object?[] { Filter.ColorCount }
            );
            (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Colors refresh failed");
            SetStatusMessageFromKey(
                nameof(LocalizationService.StatusColorsRefreshError),
                new object?[] { ex.Message }
            );
        }
        finally
        {
            _isRefreshing = false;
            (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    private IReadOnlyList<string?> GetSinceValues()
    {
        var q = SelectedTimeRangeQuery;
        return q == "all" ? new[] { "daily", "weekly", "monthly" } : new[] { q };
    }

    private void FlushBatch(
        List<GithubTrendingRepository> batch,
        ObservableCollection<GithubTrendingRepository> target
    )
    {
        var wasEmpty = target.Count == 0;
        foreach (var item in batch)
            target.Add(item);
        batch.Clear();
        OnPropertyChanged(nameof(TrendingCount));
        OnPropertyChanged(nameof(TrendingLabel));
        if (wasEmpty)
        {
            OnPropertyChanged(nameof(ShowTrendingContent));
            OnPropertyChanged(nameof(ShowTrendingLoading));
        }
    }
}
