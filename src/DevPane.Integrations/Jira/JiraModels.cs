namespace DevPane.Integrations.Jira;

/// <summary>Jira's three status groups; every workflow status belongs to one.</summary>
public enum JiraStatusCategory
{
    ToDo,
    InProgress,
    Done,
}

/// <summary>What's needed to call a Jira Cloud site.</summary>
/// <param name="Site">The site's host name, such as "acme.atlassian.net".</param>
/// <param name="ApiBase">
/// Where API requests go: the site itself for classic API tokens, or Atlassian's gateway
/// (https://api.atlassian.com/ex/jira/{cloudId}) for scoped API tokens.
/// </param>
public sealed record JiraConnection(string Site, string Email, string Token, string ApiBase);

/// <param name="IssueType">The issue type's name, such as "Bug", "Task", or "Story".</param>
/// <param name="HierarchyLevel">1 for epics, 0 for standard issues, -1 for subtasks.</param>
/// <param name="Priority">The priority's name, such as "High", or null if the site doesn't use priorities.</param>
public sealed record JiraIssue(
    string Key,
    string Summary,
    string Status,
    JiraStatusCategory Category,
    string IssueType,
    int HierarchyLevel,
    string? Priority,
    DateOnly? DueDate,
    DateTimeOffset? Updated,
    string ProjectKey,
    string ProjectName);

/// <param name="HasMore">True when the search matched more issues than were returned.</param>
public sealed record JiraSearchResult(IReadOnlyList<JiraIssue> Issues, bool HasMore);

public sealed record JiraProject(string Key, string Name);

/// <summary>A Jira call failed in a way the caller can explain to the user.</summary>
public class JiraException(string message) : Exception(message);

/// <summary>Jira rejected the email and API token (HTTP 401), usually because the token expired or was revoked.</summary>
public sealed class JiraUnauthorizedException(string message) : JiraException(message);

/// <summary>The request was valid but not allowed or not understood, for example a search Jira can't run (HTTP 400 or 403).</summary>
public sealed class JiraRequestException(string message) : JiraException(message);

public sealed class JiraRateLimitException(DateTimeOffset retryAt)
    : JiraException($"Jira's rate limit was reached. Try again at {retryAt:u}.")
{
    public DateTimeOffset RetryAt { get; } = retryAt;
}
