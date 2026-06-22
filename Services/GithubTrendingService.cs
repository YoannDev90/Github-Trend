using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Github_Trend.Database;
using Github_Trend.Utils;
using Serilog;

namespace Github_Trend.Services;

public sealed class GithubTrendingService : IGithubTrendingService, IAsyncDisposable
{
    private static readonly TimeSpan CacheTtl = Constants.Trending.TrendingCacheTtl;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly SemaphoreSlim _enrichmentLimiter;
    private readonly AppDatabase _db;
    private readonly IRepositoryDetailsService _detailsService;
    private readonly HttpClient _http;

    public GithubTrendingService(
        AppDatabase db,
        IRepositoryDetailsService detailsService,
        HttpClient? httpClient = null
    )
    {
        _db = db;
        _detailsService = detailsService;
        _http = httpClient ?? HttpClientFactory.Create();
        _enrichmentLimiter = new SemaphoreSlim(Constants.Trending.MaxParallelEnrichmentRequests);
    }

    public async Task<List<GithubTrendingRepository>> FetchAsync(
        bool force = false,
        string? since = null,
        string? language = null
    )
    {
        var cacheKey = BuildCacheKey(since, language);
        Log.Information(
            "Trending fetch started (force={Force}, since={Since}, language={Language})",
            force,
            since ?? "<null>",
            language ?? "<null>"
        );

        if (!force)
        {
            try
            {
                var cachedJson = await _db.GetTrendingCacheAsync(cacheKey);
                if (cachedJson is not null)
                {
                    var cached = DeserializeTrending(cachedJson);
                    if (cached is not null)
                    {
                        Log.Information("Trending cache hit ({Count}) for {Key}", cached.Count, cacheKey);
                        return await EnrichTrendingAsync(cached);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to read trending cache for {Key}", cacheKey);
            }
        }

        var url = BuildTrendingUrl(since, language);

        try
        {
            var json = await _http.GetStringAsync(url);
            var trending = DeserializeTrending(json) ?? new List<GithubTrendingRepository>();
            Log.Information("Trending network fetch ok ({Count}) for {Key}", trending.Count, cacheKey);

            try
            {
                await _db.SetTrendingCacheAsync(cacheKey, since ?? "", language, json, CacheTtl);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to write trending cache for {Key}", cacheKey);
            }

            return await EnrichTrendingAsync(trending);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Trending network fetch failed for {Key}", cacheKey);

            try
            {
                var staleJson = await _db.GetTrendingCacheAsync(cacheKey);
                if (staleJson is not null)
                {
                    var stale = DeserializeTrending(staleJson);
                    if (stale is not null)
                    {
                        Log.Warning("Trending stale-cache fallback ({Count}) for {Key}", stale.Count, cacheKey);
                        return await EnrichTrendingAsync(stale);
                    }
                }
            }
            catch (Exception cacheEx)
            {
                Log.Debug(cacheEx, "Failed to read stale trending cache for {Key}", cacheKey);
            }

            throw;
        }
    }

    public async IAsyncEnumerable<GithubTrendingRepository> StreamAsync(
        bool force = false,
        string? since = null,
        string? language = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var cacheKey = BuildCacheKey(since, language);

        List<GithubTrendingRepository>? repositories = null;

        if (!force)
        {
            try
            {
                var cachedJson = await _db.GetTrendingCacheAsync(cacheKey);
                if (cachedJson is not null)
                {
                    repositories = DeserializeTrending(cachedJson);
                    if (repositories is not null)
                        Log.Information("Trending cache hit ({Count}) for {Key}", repositories.Count, cacheKey);
                }
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to read trending cache for {Key}", cacheKey);
            }
        }

        if (repositories is null)
        {
            var url = BuildTrendingUrl(since, language);

            try
            {
                var json = await _http.GetStringAsync(url, cancellationToken);
                repositories = DeserializeTrending(json) ?? new List<GithubTrendingRepository>();
                Log.Information("Trending network fetch ok ({Count}) for {Key}", repositories.Count, cacheKey);

                try
                {
                    await _db.SetTrendingCacheAsync(cacheKey, since ?? "", language, json, CacheTtl);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Failed to write trending cache for {Key}", cacheKey);
                }
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Trending network fetch failed for {Key}", cacheKey);

                try
                {
                    var staleJson = await _db.GetTrendingCacheAsync(cacheKey);
                    if (staleJson is not null)
                    {
                        repositories = DeserializeTrending(staleJson);
                        if (repositories is not null)
                            Log.Warning("Trending stale-cache fallback ({Count}) for {Key}", repositories.Count, cacheKey);
                    }
                }
                catch (Exception cacheEx)
                {
                    Log.Debug(cacheEx, "Failed to read stale trending cache for {Key}", cacheKey);
                }

                if (repositories is null)
                    yield break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

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
                        repositories.Select(repo =>
                            EnrichAndWriteAsync(repo, channel.Writer, cancellationToken)
                        )
                    );
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("Trending stream cancelled for {Key}", cacheKey);
                }
                finally
                {
                    channel.Writer.TryComplete();
                }
            },
            cancellationToken
        );

        await foreach (var repo in channel.Reader.ReadAllAsync(cancellationToken))
            yield return repo;
    }

    private async Task EnrichAndWriteAsync(
        GithubTrendingRepository repo,
        ChannelWriter<GithubTrendingRepository> writer,
        CancellationToken cancellationToken
    )
    {
        await _enrichmentLimiter.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var enriched = await _detailsService.EnrichAsync(repo, cancellationToken);
            await writer.WriteAsync(enriched, cancellationToken);
            await Task.Delay(
                Constants.Trending.InterEnrichmentDelayMilliseconds,
                cancellationToken
            );
        }
        finally
        {
            _enrichmentLimiter.Release();
        }
    }

    private async Task<List<GithubTrendingRepository>> EnrichTrendingAsync(
        IEnumerable<GithubTrendingRepository> repositories
    )
    {
        var enrichTasks = repositories.Select(async repo =>
        {
            await _enrichmentLimiter.WaitAsync();
            try
            {
                return await _detailsService.EnrichAsync(repo);
            }
            finally
            {
                _enrichmentLimiter.Release();
            }
        });

        var enriched = await Task.WhenAll(enrichTasks);
        return enriched.ToList();
    }

    private static List<GithubTrendingRepository>? DeserializeTrending(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<GithubTrendingRepository>>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to deserialize trending JSON");
            return null;
        }
    }

    private static string BuildCacheKey(string? since, string? language)
    {
        return $"trending-{since ?? "all"}-{language ?? "all"}";
    }

    private static string BuildTrendingUrl(string? since, string? language)
    {
        var url = Constants.GitHubTrendingUrl;
        var queryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(since))
            queryParts.Add($"since={Uri.EscapeDataString(since)}");
        if (!string.IsNullOrWhiteSpace(language))
            queryParts.Add($"language={Uri.EscapeDataString(language)}");
        if (queryParts.Count > 0)
            url += "?" + string.Join("&", queryParts);
        return url;
    }

    public async ValueTask DisposeAsync()
    {
        _enrichmentLimiter.Dispose();
        _http.Dispose();
        await ValueTask.CompletedTask;
    }
}
