namespace DevPane.Integrations.GitHub;

/// <summary>Combined result of a pull request's CI checks on its latest commit.</summary>
public enum CheckState
{
    None,
    Pending,
    Success,
    Failure,
}

public enum ReviewDecision
{
    None,
    ReviewRequired,
    Approved,
    ChangesRequested,
}

public enum RunState
{
    Other,
    Queued,
    InProgress,
    Succeeded,
    Failed,
    Canceled,
}

/// <param name="Repository">Owner and name, such as <c>microsoft/vscode</c>.</param>
/// <param name="UpdatedAt">When the pull request last changed, or null if GitHub didn't say.</param>
public sealed record PullRequestItem(
    int Number,
    string Title,
    string Url,
    string Repository,
    string? Author,
    bool IsDraft,
    CheckState Checks,
    ReviewDecision Review,
    DateTimeOffset? UpdatedAt);

public sealed record IssueItem(int Number, string Title, string Url, string Repository);

/// <param name="UpdatedAt">When the run last changed, or null if GitHub didn't say.</param>
public sealed record WorkflowRunItem(
    string Title,
    string WorkflowName,
    string Branch,
    RunState State,
    string Url,
    DateTimeOffset? UpdatedAt);

/// <param name="TotalCount">Every match on GitHub, which can be more than <paramref name="Items"/> holds.</param>
public sealed record SearchResult<T>(int TotalCount, IReadOnlyList<T> Items);

/// <param name="AvatarUrl">The user's profile picture, or an empty string if GitHub didn't send one.</param>
/// <param name="WorkflowRuns">Latest runs when the overview covers one repository; otherwise null.</param>
public sealed record GitHubOverview(
    string Login,
    string AvatarUrl,
    SearchResult<PullRequestItem> ReviewRequests,
    SearchResult<PullRequestItem> MyPullRequests,
    SearchResult<IssueItem> AssignedIssues,
    IReadOnlyList<WorkflowRunItem>? WorkflowRuns);

/// <param name="Level">GitHub's shading level: 0 for no contributions, up to 4 for the busiest days.</param>
public sealed record ContributionDay(DateOnly Date, int Count, int Level);

/// <summary>The user's contribution calendar for the past year, one entry per day, oldest first.</summary>
public sealed record ContributionCalendar(string Login, int TotalContributions, IReadOnlyList<ContributionDay> Days)
{
    public int CountOn(DateOnly date) => Days.FirstOrDefault(day => day.Date == date)?.Count ?? 0;

    /// <summary>Consecutive days with contributions, ending today, or yesterday if today has none yet.</summary>
    public int CurrentStreak(DateOnly today)
    {
        var counts = Days.ToDictionary(day => day.Date, day => day.Count);
        var date = counts.GetValueOrDefault(today) > 0 ? today : today.AddDays(-1);
        int streak = 0;
        while (counts.GetValueOrDefault(date) > 0)
        {
            streak++;
            date = date.AddDays(-1);
        }

        return streak;
    }

    public int LongestStreak()
    {
        int longest = 0;
        int current = 0;
        foreach (var day in Days)
        {
            current = day.Count > 0 ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }

        return longest;
    }
}

/// <summary>A GitHub call failed in a way the caller can explain to the user.</summary>
public class GitHubException(string message) : Exception(message);

/// <summary>GitHub rejected the access token (HTTP 401), usually because it was revoked.</summary>
/// <param name="detail">GitHub's own explanation, such as "Bad credentials".</param>
/// <param name="requestId">GitHub's request ID, which GitHub Support can look up.</param>
public sealed class GitHubUnauthorizedException(string detail, string requestId)
    : GitHubException($"GitHub rejected the sign-in: {detail} (request ID {requestId}).");

public sealed class GitHubRateLimitException(DateTimeOffset resetAt)
    : GitHubException($"GitHub's rate limit was reached. It resets at {resetAt:u}.")
{
    public DateTimeOffset ResetAt { get; } = resetAt;
}
