using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia.Threading;
using Github_Trend.Localization;
using Github_Trend.Services;
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

        var token = cts.Token;

        try
        {
            var sinceValues = GetSinceValues();
            var languages = Filter.SelectedLanguageFilters;

            Log.Information(
                "Trending refresh start since=[{Since}] languages=[{Languages}]",
                string.Join(',', sinceValues),
                string.Join(',', languages.Select(x => x ?? "<all>"))
            );

            // Load slugs while old items are still displayed
            var dismissedSlugs = await _db.GetDismissedSlugsAsync();
            var starredSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var watchedSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

            // Phase 1: load cached trending (old items still visible, no flash)
            var cachedEntries = new List<(string key, GithubTrendingRepository repo)>();
            var slugIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var since in sinceValues)
            {
                foreach (var lang in languages)
                {
                    var cached = await _trendingService.TryGetCachedTrendingAsync(since, lang);
                    if (cached is null) continue;
                    foreach (var repo in cached)
                    {
                        var key = repo.Repository ?? repo.Name;
                        if (key is null || slugIndex.ContainsKey(key)) continue;
                        var slug = RepositoryUrlParser.GetSlug(repo.Repository);
                        if (!string.IsNullOrWhiteSpace(slug) && dismissedSlugs.Contains(slug))
                            continue;
                        slugIndex[key] = 0; // placeholder, real index after clear
                        cachedEntries.Add((key, repo));
                    }
                }
            }

            // Swap: clear old items and show cached in one go
            foreach (var repo in TrendingRepositories)
                repo.Dispose();
            TrendingRepositories.Clear();

            foreach (var (key, repo) in cachedEntries)
            {
                var slug = RepositoryUrlParser.GetSlug(repo.Repository);
                var visual = ApplyLanguageBrush(repo);
                if (!string.IsNullOrWhiteSpace(slug))
                {
                    visual.IsStarred = starredSlugs.Contains(slug);
                    visual.IsWatched = watchedSlugs.Contains(slug);
                    visual.IsDismissed = dismissedSlugs.Contains(slug);
                }
                slugIndex[key] = TrendingRepositories.Count;
                TrendingRepositories.Add(visual);
            }

            OnPropertyChanged(nameof(ShowTrendingContent));
            OnPropertyChanged(nameof(ShowTrendingEmpty));
            OnPropertyChanged(nameof(TrendingCount));
            OnPropertyChanged(nameof(TrendingLabel));

            var preCount = TrendingRepositories.Count;
            if (preCount > 0)
                Log.Information("Trending cache shown: {Count}", preCount);

            // Phase 2: stream enriched data (loading = true, but spinner hidden while items exist)
            IsTrendingLoading = true;

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
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Trending stream producer failed");
                    }
                    finally
                    {
                        channel.Writer.TryComplete();
                    }
                },
                token
            );

            var batch = new List<GithubTrendingRepository>(
                AppConfig.Trending.MaxParallelEnrichmentRequests * 5
            );
            var batchLock = new object();
            var flushTimer = new System.Timers.Timer(300) { AutoReset = false };
            flushTimer.Elapsed += (_, _) =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    lock (batchLock)
                    {
                        if (batch.Count > 0)
                            FlushBatch(batch, TrendingRepositories);
                    }
                });
            };

            var replacedItems = new List<GithubTrendingRepository>();

            await foreach (var repo in channel.Reader.ReadAllAsync(token))
            {
                var key = repo.Repository ?? repo.Name;
                if (key is null) continue;

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

                if (slugIndex.TryGetValue(key, out var existingIdx))
                {
                    // Replace cached item with enriched version (defer disposal to avoid UI race)
                    var old = TrendingRepositories[existingIdx];
                    TrendingRepositories[existingIdx] = visual;
                    replacedItems.Add(old);
                }
                else
                {
                    // New item (not in cache) — add via batch
                    lock (batchLock)
                    {
                        slugIndex[key] = TrendingRepositories.Count + batch.Count;
                        batch.Add(visual);
                        flushTimer.Stop();
                        flushTimer.Start();
                    }
                }
            }

            flushTimer.Stop();
            using (flushTimer)
            {
                lock (batchLock)
                {
                    if (batch.Count > 0)
                        FlushBatch(batch, TrendingRepositories);
                }
            }

            // Dispose replaced items after the stream ends (UI has released old bitmaps)
            foreach (var d in replacedItems)
                d.Dispose();

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
            RebuildLanguageBrushCache(catalog);
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
