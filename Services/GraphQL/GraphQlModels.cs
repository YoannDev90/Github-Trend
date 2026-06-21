using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Github_Trend.Services.GraphQL;

public sealed class GraphQlRequest
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    [JsonPropertyName("variables")]
    public Dictionary<string, object>? Variables { get; set; }
}

public sealed class GraphQlResponse<T>
{
    [JsonPropertyName("data")]
    public T? Data { get; set; }

    [JsonPropertyName("errors")]
    public List<GraphQlError>? Errors { get; set; }

    public bool HasErrors => Errors is { Count: > 0 };
}

public sealed class GraphQlError
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("path")]
    public List<string>? Path { get; set; }
}

// --- Repository Details ---

public sealed class RepositoryDetailsResponse
{
    [JsonPropertyName("repository")]
    public RepositoryNode? Repository { get; set; }
}

public sealed class RepositoryNode
{
    [JsonPropertyName("nameWithOwner")]
    public string? NameWithOwner { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("homepageUrl")]
    public string? HomepageUrl { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("licenseInfo")]
    public LicenseInfoNode? LicenseInfo { get; set; }

    [JsonPropertyName("repositoryTopics")]
    public RepositoryTopicsConnection? RepositoryTopics { get; set; }

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("defaultBranchRef")]
    public DefaultBranchRefNode? DefaultBranchRef { get; set; }
}

public sealed class LicenseInfoNode
{
    [JsonPropertyName("spdxId")]
    public string? SpdxId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class RepositoryTopicsConnection
{
    [JsonPropertyName("nodes")]
    public List<TopicNode>? Nodes { get; set; }
}

public sealed class TopicNode
{
    [JsonPropertyName("topic")]
    public TopicValue? Topic { get; set; }
}

public sealed class TopicValue
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class DefaultBranchRefNode
{
    [JsonPropertyName("target")]
    public CommitTarget? Target { get; set; }
}

public sealed class CommitTarget
{
    [JsonPropertyName("committedDate")]
    public string? CommittedDate { get; set; }
}

// --- Contributors ---

public sealed class ContributorsResponse
{
    [JsonPropertyName("repository")]
    public ContributorsRepositoryNode? Repository { get; set; }
}

public sealed class ContributorsRepositoryNode
{
    [JsonPropertyName("collaborators")]
    public CollaboratorsConnection? Collaborators { get; set; }
}

public sealed class CollaboratorsConnection
{
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("nodes")]
    public List<CollaboratorNode>? Nodes { get; set; }
}

public sealed class CollaboratorNode
{
    [JsonPropertyName("login")]
    public string? Login { get; set; }

    [JsonPropertyName("avatarUrl")]
    public string? AvatarUrl { get; set; }
}

// --- Star ---

public sealed class StarResponse
{
    [JsonPropertyName("repository")]
    public StarRepositoryNode? Repository { get; set; }
}

public sealed class StarRepositoryNode
{
    [JsonPropertyName("viewerHasStarred")]
    public bool IsStarredByViewer { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

public sealed class AddStarResponse
{
    [JsonPropertyName("addStar")]
    public AddStarPayload? AddStar { get; set; }
}

public sealed class AddStarPayload
{
    [JsonPropertyName("starrable")]
    public StarrableNode? Starrable { get; set; }
}

public sealed class StarrableNode
{
    [JsonPropertyName("stargazerCount")]
    public int StargazerCount { get; set; }

    [JsonPropertyName("viewerHasStarred")]
    public bool IsStarredByViewer { get; set; }
}

// --- Viewer Starred Repos ---

public sealed class ViewerStarredResponse
{
    [JsonPropertyName("viewer")]
    public ViewerNode? Viewer { get; set; }
}

public sealed class ViewerNode
{
    [JsonPropertyName("starredRepositories")]
    public StarredReposConnection? StarredRepositories { get; set; }
}

public sealed class StarredReposConnection
{
    [JsonPropertyName("pageInfo")]
    public PageInfoNode? PageInfo { get; set; }

    [JsonPropertyName("nodes")]
    public List<StarredRepoNode>? Nodes { get; set; }
}

public sealed class PageInfoNode
{
    [JsonPropertyName("hasNextPage")]
    public bool HasNextPage { get; set; }

    [JsonPropertyName("endCursor")]
    public string? EndCursor { get; set; }
}

public sealed class StarredRepoNode
{
    [JsonPropertyName("nameWithOwner")]
    public string? NameWithOwner { get; set; }
}

// --- Star Lists ---

public sealed class ViewerStarListsResponse
{
    [JsonPropertyName("viewer")]
    public ViewerListsNode? Viewer { get; set; }
}

public sealed class ViewerListsNode
{
    [JsonPropertyName("lists")]
    public StarListConnection? Lists { get; set; }
}

public sealed class StarListConnection
{
    [JsonPropertyName("nodes")]
    public List<StarListNode>? Nodes { get; set; }
}

public sealed class StarListNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("isPrivate")]
    public bool IsPrivate { get; set; }

    [JsonPropertyName("items")]
    public StarListItemsConnection? Items { get; set; }
}

public sealed class StarListItemsConnection
{
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }
}

public sealed class ItemListMembershipsResponse
{
    [JsonPropertyName("node")]
    public ItemListNode? Node { get; set; }
}

public sealed class ItemListNode
{
    [JsonPropertyName("listIds")]
    public List<string>? ListIds { get; set; }
}

public sealed class CreateStarListResponse
{
    [JsonPropertyName("createUserList")]
    public CreateStarListPayload? CreateUserList { get; set; }
}

public sealed class CreateStarListPayload
{
    [JsonPropertyName("list")]
    public StarListNode? List { get; set; }
}

public sealed class UpdateRepoStarListsResponse
{
    [JsonPropertyName("updateUserListsForItem")]
    public UpdateRepoStarListsPayload? UpdateUserListsForItem { get; set; }
}

public sealed class UpdateRepoStarListsPayload
{
    [JsonPropertyName("item")]
    public UpdateRepoItemNode? Item { get; set; }
}

public sealed class UpdateRepoItemNode
{
    [JsonPropertyName("nameWithOwner")]
    public string? NameWithOwner { get; set; }
}
