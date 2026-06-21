using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Github_Trend.Database;
using Github_Trend.Services.GraphQL;
using Serilog;

namespace Github_Trend.Services;

public sealed class GitHubGraphQlService : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly GitHubAuthenticationService _authService;
    private readonly GitHubRateLimitService _rateLimitService;
    private readonly AppDatabase _db;

    public GitHubGraphQlService(
        GitHubAuthenticationService authService,
        GitHubRateLimitService rateLimitService,
        AppDatabase db,
        HttpClient? httpClient = null
    )
    {
        _authService = authService;
        _rateLimitService = rateLimitService;
        _db = db;
        _httpClient = httpClient ?? CreateHttpClient();
    }

    public async Task<RepositoryEnrichmentData?> GetRepositoryDetailsAsync(
        string owner,
        string name,
        CancellationToken ct = default
    )
    {
        if (_rateLimitService.IsInCooldown)
        {
            Log.Information(
                "Skipping GraphQL repo details for {Owner}/{Name} during rate-limit cooldown",
                owner,
                name
            );
            return null;
        }

        try
        {
            var query = GraphQlQueryLoader.Load("RepositoryDetails.graphql");
            var response = await ExecuteAsync<RepositoryDetailsResponse>(
                query,
                new Dictionary<string, object>
                {
                    ["owner"] = owner,
                    ["name"] = name,
                },
                ct
            );

            var repo = response?.Repository;
            if (repo is null) return null;

            var topics = new List<string>();
            if (repo.RepositoryTopics?.Nodes is not null)
            {
                foreach (var node in repo.RepositoryTopics.Nodes)
                {
                    if (!string.IsNullOrWhiteSpace(node.Topic?.Name))
                        topics.Add(node.Topic.Name!);
                }
            }

            var license = NormalizeLicense(repo.LicenseInfo);

            return new RepositoryEnrichmentData
            {
                HtmlUrl = repo.Url ?? $"https://github.com/{owner}/{name}",
                Description = repo.Description,
                License = license,
                Topics = topics,
                UpdatedAt = repo.UpdatedAt,
            };
        }
        catch (GraphQlRateLimitException)
        {
            Log.Warning("GraphQL rate limit hit for {Owner}/{Name}", owner, name);
            return null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "GraphQL repo details failed for {Owner}/{Name}", owner, name);
            return null;
        }
    }

    public async Task<ContributorFetchData> GetContributorsAsync(
        string owner,
        string name,
        int first,
        CancellationToken ct = default
    )
    {
        if (_rateLimitService.IsInCooldown)
        {
            return new ContributorFetchData(Array.Empty<ContributorData>(), 0);
        }

        try
        {
            var query = GraphQlQueryLoader.Load("RepositoryContributors.graphql");
            var response = await ExecuteAsync<ContributorsResponse>(
                query,
                new Dictionary<string, object>
                {
                    ["owner"] = owner,
                    ["name"] = name,
                    ["first"] = first,
                },
                ct
            );

            var collaborators = response?.Repository?.Collaborators;
            if (collaborators is null)
                return new ContributorFetchData(Array.Empty<ContributorData>(), 0);

            var contributors = new List<ContributorData>();
            if (collaborators.Nodes is not null)
            {
                foreach (var node in collaborators.Nodes)
                {
                    if (!string.IsNullOrWhiteSpace(node.Login))
                    {
                        contributors.Add(
                            new ContributorData(node.Login!, node.AvatarUrl)
                        );
                    }
                }
            }

            return new ContributorFetchData(contributors, collaborators.TotalCount);
        }
        catch (GraphQlRateLimitException)
        {
            return new ContributorFetchData(Array.Empty<ContributorData>(), 0);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "GraphQL contributors failed for {Owner}/{Name}", owner, name);
            return new ContributorFetchData(Array.Empty<ContributorData>(), 0);
        }
    }

    public async Task<bool?> IsStarredAsync(
        string owner,
        string name,
        CancellationToken ct = default
    )
    {
        if (_rateLimitService.IsInCooldown) return null;

        try
        {
            var query = GraphQlQueryLoader.Load("ViewerStarred.graphql");
            var op = GraphQlQueryLoader.ExtractQuery(query, "IsStarred");
            var response = await ExecuteAsync<StarResponse>(
                op,
                new Dictionary<string, object>
                {
                    ["owner"] = owner,
                    ["name"] = name,
                },
                ct
            );

            return response?.Repository?.IsStarredByViewer;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "GraphQL isStarred check failed for {Owner}/{Name}", owner, name);
            return null;
        }
    }

    public async Task<bool> ToggleStarAsync(
        string owner,
        string name,
        bool currentlyStarred,
        CancellationToken ct = default
    )
    {
        var starQuery = GraphQlQueryLoader.Load("StarRepository.graphql");
        var op = currentlyStarred
            ? GraphQlQueryLoader.ExtractMutation(starQuery, "UnstarRepository")
            : GraphQlQueryLoader.ExtractMutation(starQuery, "StarRepository");

        // We need the repository ID for the mutation, fetch it first
        var detailsQuery = GraphQlQueryLoader.Load("RepositoryDetails.graphql");
        var detailsResponse = await ExecuteAsync<RepositoryDetailsResponse>(
            detailsQuery,
            new Dictionary<string, object> { ["owner"] = owner, ["name"] = name },
            ct
        );

        // The GraphQL query doesn't return ID directly, we need to get it via a separate query
        var idQuery = $"{{ repository(owner: \"{owner}\", name: \"{name}\") {{ id }} }}";
        var idResponse = await ExecuteRawAsync<GraphIdResponse>(
            idQuery,
            ct
        );

        if (idResponse?.Repository?.Id is null)
            throw new InvalidOperationException($"Could not get repository ID for {owner}/{name}");

        var response = await ExecuteAsync<AddStarResponse>(
            op,
            new Dictionary<string, object>
            {
                ["repositoryId"] = idResponse.Repository.Id,
            },
            ct
        );

        return response?.AddStar?.Starrable?.IsStarredByViewer ?? false;
    }

    public async Task<bool> ToggleWatchAsync(
        string owner,
        string name,
        bool currentlyWatched,
        CancellationToken ct = default
    )
    {
        var nodeId = await GetRepositoryNodeIdAsync(owner, name, ct);
        if (nodeId is null)
            throw new InvalidOperationException($"Could not get repository ID for {owner}/{name}");

        var query = GraphQlQueryLoader.Load("ViewerStarred.graphql");
        var op = currentlyWatched
            ? GraphQlQueryLoader.ExtractMutation(query, "UnwatchRepository")
            : GraphQlQueryLoader.ExtractMutation(query, "WatchRepository");

        var watchResponse = await ExecuteAsync<UpdateSubscriptionResponse>(
            op,
            new Dictionary<string, object>
            {
                ["subscribableId"] = nodeId,
            },
            ct
        );

        return watchResponse?.UpdateSubscription?.Subscribable?.NameWithOwner is not null;
    }

    public async Task<string?> GetRepositoryNodeIdAsync(
        string owner,
        string name,
        CancellationToken ct
    )
    {
        var idQuery = $"{{ repository(owner: \"{owner}\", name: \"{name}\") {{ id }} }}";
        var idResponse = await ExecuteRawAsync<GraphIdResponse>(idQuery, ct);
        return idResponse?.Repository?.Id;
    }

    public async Task<HashSet<string>> GetAllStarredSlugsAsync(CancellationToken ct = default)
    {
        var allSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? cursor = null;

        for (var page = 0; page < 10; page++)
        {
            if (_rateLimitService.IsInCooldown) break;

            try
            {
        var query = GraphQlQueryLoader.Load("ViewerStarred.graphql");
                var op = GraphQlQueryLoader.ExtractQuery(query, "ViewerStarredRepositories");

                var variables = new Dictionary<string, object>
                {
                    ["first"] = 100,
                };
                if (cursor is not null)
                    variables["after"] = cursor;

                var response = await ExecuteAsync<ViewerStarredResponse>(op, variables, ct);

                var connection = response?.Viewer?.StarredRepositories;
                if (connection?.Nodes is null || connection.Nodes.Count == 0)
                    break;

                foreach (var node in connection.Nodes)
                {
                    if (!string.IsNullOrWhiteSpace(node.NameWithOwner))
                        allSlugs.Add(node.NameWithOwner);
                }

                if (!connection.PageInfo?.HasNextPage ?? true)
                    break;

                cursor = connection.PageInfo?.EndCursor;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to fetch starred repos page {Page}", page);
                break;
            }
        }

        return allSlugs;
    }

    public async Task<List<StarListNode>> GetStarListsAsync(CancellationToken ct = default)
    {
        var query = GraphQlQueryLoader.Load("StarLists.graphql");
        var op = GraphQlQueryLoader.ExtractQuery(query, "ViewerStarLists");
        var response = await ExecuteAsync<ViewerStarListsResponse>(op, null, ct);
        return response?.Viewer?.Lists?.Nodes ?? new List<StarListNode>();
    }

    public async Task<StarListNode?> CreateStarListAsync(
        string name, string? description, bool isPrivate, CancellationToken ct = default
    )
    {
        var query = GraphQlQueryLoader.Load("StarLists.graphql");
        var op = GraphQlQueryLoader.ExtractMutation(query, "CreateStarList");
        var response = await ExecuteAsync<CreateStarListResponse>(
            op,
            new Dictionary<string, object>
            {
                ["name"] = name,
                ["description"] = description ?? "",
                ["isPrivate"] = isPrivate,
            },
            ct
        );
        return response?.CreateUserList?.List;
    }

    public async Task<bool> AddRepositoryToStarListsAsync(
        string nodeId, List<string> listIds, CancellationToken ct = default
    )
    {
        var query = GraphQlQueryLoader.Load("StarLists.graphql");
        var op = GraphQlQueryLoader.ExtractMutation(query, "UpdateRepoStarLists");
        var response = await ExecuteAsync<UpdateRepoStarListsResponse>(
            op,
            new Dictionary<string, object>
            {
                ["itemId"] = nodeId,
                ["listIds"] = listIds,
            },
            ct
        );
        return response?.UpdateUserListsForItem?.Item?.NameWithOwner is not null;
    }

    public async Task<List<string>> GetItemListMembershipsAsync(
        string nodeId, CancellationToken ct = default
    )
    {
        var query = GraphQlQueryLoader.Load("StarLists.graphql");
        var op = GraphQlQueryLoader.ExtractQuery(query, "ItemListMemberships");
        var response = await ExecuteAsync<ItemListMembershipsResponse>(
            op,
            new Dictionary<string, object> { ["itemId"] = nodeId },
            ct
        );
        return response?.Node?.ListIds ?? new List<string>();
    }

    private static string? NormalizeLicense(LicenseInfoNode? license)
    {
        var value = license?.SpdxId;
        if (
            !string.IsNullOrWhiteSpace(value)
            && !string.Equals(value, "NOASSERTION", StringComparison.OrdinalIgnoreCase)
        )
            return value;

        value = license?.Name;
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        return null;
    }

    private async Task<T?> ExecuteAsync<T>(
        string query,
        Dictionary<string, object>? variables,
        CancellationToken ct
    ) where T : class
    {
        var request = new GraphQlRequest
        {
            Query = query,
            Variables = variables,
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://api.github.com/graphql")
        {
            Content = content,
        };

        var token = await _authService.GetAccessTokenAsync(true);
        if (!string.IsNullOrWhiteSpace(token))
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        requestMessage.Headers.UserAgent.ParseAdd(Constants.GitHub.UserAgent);
        requestMessage.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json")
        );

        var response = await _httpClient.SendAsync(requestMessage, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        _rateLimitService.TrackFromHeaders(response);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await _authService.RefreshCurrentAsync();
            throw new GraphQlAuthException("Unauthorized after token refresh");
        }

        if (response.StatusCode == (System.Net.HttpStatusCode)403
            || response.StatusCode == (System.Net.HttpStatusCode)429)
        {
            _rateLimitService.RegisterCooldown(response);
            throw new GraphQlRateLimitException();
        }

        response.EnsureSuccessStatusCode();

        var graphResponse = JsonSerializer.Deserialize<GraphQlResponse<T>>(
            responseBody,
            JsonOptions
        );

        if (graphResponse?.HasErrors == true)
        {
            var errorMessages = string.Join("; ", graphResponse.Errors!.ConvertAll(e => e.Message));

            if (errorMessages.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
                throw new GraphQlRateLimitException();

            if (errorMessages.Contains("permission", StringComparison.OrdinalIgnoreCase)
                || errorMessages.Contains("OAuth App access restrictions", StringComparison.OrdinalIgnoreCase))
                Log.Debug("GraphQL non-critical error (ignored): {Errors}", errorMessages);
            else
                Log.Warning("GraphQL errors: {Errors}", errorMessages);
        }

        return graphResponse?.Data;
    }

    private async Task<T?> ExecuteRawAsync<T>(string query, CancellationToken ct)
        where T : class
    {
        var request = new GraphQlRequest { Query = query };
        var json = JsonSerializer.Serialize(request, JsonOptions);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://api.github.com/graphql")
        {
            Content = content,
        };

        var token = await _authService.GetAccessTokenAsync(true);
        if (!string.IsNullOrWhiteSpace(token))
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        requestMessage.Headers.UserAgent.ParseAdd(Constants.GitHub.UserAgent);

        var response = await _httpClient.SendAsync(requestMessage, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        _rateLimitService.TrackFromHeaders(response);

        if (!response.IsSuccessStatusCode)
            return default;

        var graphResponse = JsonSerializer.Deserialize<GraphQlResponse<T>>(
            responseBody,
            JsonOptions
        );
        return graphResponse?.Data;
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };
        return new HttpClient(handler);
    }

    public async ValueTask DisposeAsync()
    {
        _httpClient.Dispose();
        await ValueTask.CompletedTask;
    }
}

// --- Data transfer objects ---

public sealed class RepositoryEnrichmentData
{
    public string HtmlUrl { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? License { get; set; }
    public List<string> Topics { get; set; } = new();
    public string? UpdatedAt { get; set; }
}

public sealed class ContributorData(string login, string? avatarUrl)
{
    public string Login { get; } = login;
    public string? AvatarUrl { get; } = avatarUrl;
}

public sealed class ContributorFetchData(IReadOnlyList<ContributorData> contributors, int totalCount)
{
    public IReadOnlyList<ContributorData> Contributors { get; } = contributors;
    public int TotalCount { get; } = totalCount;
}

internal sealed class GraphIdResponse
{
    [JsonPropertyName("repository")]
    public GraphIdRepository? Repository { get; set; }
}

internal sealed class GraphIdRepository
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

internal sealed class UpdateSubscriptionResponse
{
    [JsonPropertyName("updateSubscription")]
    public UpdateSubscriptionPayload? UpdateSubscription { get; set; }
}

internal sealed class UpdateSubscriptionPayload
{
    [JsonPropertyName("subscribable")]
    public SubscribableNode? Subscribable { get; set; }
}

internal sealed class SubscribableNode
{
    [JsonPropertyName("nameWithOwner")]
    public string? NameWithOwner { get; set; }
}

public sealed class GraphQlRateLimitException : Exception
{
    public GraphQlRateLimitException()
        : base("GitHub GraphQL rate limit reached") { }
}

public sealed class GraphQlAuthException : Exception
{
    public GraphQlAuthException(string message)
        : base(message) { }
}
