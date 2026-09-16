using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DevPane.Integrations.GitHub;

/// <summary>
/// Reads pull requests, issues, and workflow runs for the signed-in user. One overview costs one GraphQL request,
/// plus one REST request for workflow runs that's free when nothing changed (ETag).
/// </summary>
public sealed class GitHubClient(string accessToken)
{
    private const string GraphQlUrl = "https://api.github.com/graphql";
    private const int ItemsPerList = 5;
    private const int RecentRepositoryCount = 30;

    private const string OverviewQuery = """
        query($reviews: String!, $mine: String!, $issues: String!, $first: Int!) {
          viewer { login avatarUrl(size: 64) }
          reviews: search(query: $reviews, type: ISSUE, first: $first) { issueCount nodes { ...pr } }
          mine: search(query: $mine, type: ISSUE, first: $first) { issueCount nodes { ...pr } }
          issues: search(query: $issues, type: ISSUE, first: $first) {
            issueCount
            nodes { ... on Issue { number title url repository { nameWithOwner } } }
          }
        }
        fragment pr on PullRequest {
          number title url isDraft reviewDecision updatedAt
          repository { nameWithOwner }
          author { login }
          commits(last: 1) { nodes { commit { statusCheckRollup { state } } } }
        }
        """;

    private const string RecentRepositoriesQuery = """
        query($first: Int!) {
          viewer {
            repositories(
              first: $first
              orderBy: { field: PUSHED_AT, direction: DESC }
              affiliations: [OWNER, COLLABORATOR, ORGANIZATION_MEMBER]
              ownerAffiliations: [OWNER, COLLABORATOR, ORGANIZATION_MEMBER]
            ) { nodes { nameWithOwner } }
          }
        }
        """;

    private const string ContributionsQuery = """
        query {
          viewer {
            login
            contributionsCollection {
              contributionCalendar {
                totalContributions
                weeks { contributionDays { date contributionCount contributionLevel } }
              }
            }
          }
        }
        """;

    private const string RepositoryQuery = """
        query($owner: String!, $name: String!) { repository(owner: $owner, name: $name) { nameWithOwner } }
        """;

    private readonly Dictionary<string, (EntityTagHeaderValue ETag, IReadOnlyList<WorkflowRunItem> Runs)> _runsCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <param name="repository">Owner and name to limit results to, or null for everything the user can see.</param>
    public async Task<GitHubOverview> GetOverviewAsync(string? repository, CancellationToken cancellationToken)
    {
        string scope = repository is null ? string.Empty : $" repo:{repository}";
        using var json = await PostGraphQlAsync(OverviewQuery, new JsonObject
        {
            ["reviews"] = "is:open is:pr archived:false review-requested:@me" + scope,
            ["mine"] = "is:open is:pr archived:false author:@me" + scope,
            ["issues"] = "is:open is:issue archived:false assignee:@me" + scope,
            ["first"] = ItemsPerList,
        }, cancellationToken);

        var data = json.RootElement.GetProperty("data");
        var viewer = data.GetProperty("viewer");
        return new GitHubOverview(
            GitHubHttp.GetString(viewer, "login"),
            GitHubHttp.GetString(viewer, "avatarUrl"),
            ReadPullRequests(data.GetProperty("reviews")),
            ReadPullRequests(data.GetProperty("mine")),
            ReadIssues(data.GetProperty("issues")),
            repository is null ? null : await GetWorkflowRunsAsync(repository, cancellationToken));
    }

    /// <summary>Repositories the user pushed to most recently, for the card's repository picker.</summary>
    public async Task<IReadOnlyList<string>> GetRecentRepositoriesAsync(CancellationToken cancellationToken)
    {
        using var json = await PostGraphQlAsync(RecentRepositoriesQuery, new JsonObject { ["first"] = RecentRepositoryCount },
            cancellationToken);

        return json.RootElement.GetProperty("data").GetProperty("viewer").GetProperty("repositories").GetProperty("nodes")
            .EnumerateArray()
            .Select(node => GitHubHttp.GetString(node, "nameWithOwner"))
            .Where(name => name.Length > 0)
            .ToList();
    }

    /// <summary>The signed-in user's contribution calendar for the past year.</summary>
    public async Task<ContributionCalendar> GetContributionCalendarAsync(CancellationToken cancellationToken)
    {
        using var json = await PostGraphQlAsync(ContributionsQuery, new JsonObject(), cancellationToken);
        var viewer = json.RootElement.GetProperty("data").GetProperty("viewer");
        var calendar = viewer.GetProperty("contributionsCollection").GetProperty("contributionCalendar");

        var days = new List<ContributionDay>();
        foreach (var week in calendar.GetProperty("weeks").EnumerateArray())
        {
            foreach (var day in week.GetProperty("contributionDays").EnumerateArray())
            {
                days.Add(new ContributionDay(
                    DateOnly.ParseExact(GitHubHttp.GetString(day, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    day.GetProperty("contributionCount").GetInt32(),
                    GitHubHttp.GetString(day, "contributionLevel") switch
                    {
                        "FIRST_QUARTILE" => 1,
                        "SECOND_QUARTILE" => 2,
                        "THIRD_QUARTILE" => 3,
                        "FOURTH_QUARTILE" => 4,
                        _ => 0,
                    }));
            }
        }

        return new ContributionCalendar(
            GitHubHttp.GetString(viewer, "login"),
            calendar.GetProperty("totalContributions").GetInt32(),
            days);
    }

    /// <summary>Returns the repository's exact owner/name, or null if it doesn't exist or the user can't see it.</summary>
    public async Task<string?> FindRepositoryAsync(string repository, CancellationToken cancellationToken)
    {
        string[] parts = repository.Split('/');
        using var json = await PostGraphQlAsync(RepositoryQuery, new JsonObject { ["owner"] = parts[0], ["name"] = parts[1] },
            cancellationToken);

        string name = GitHubHttp.GetString(json.RootElement.GetProperty("data").GetProperty("repository"), "nameWithOwner");
        return name.Length > 0 ? name : null;
    }

    private async Task<IReadOnlyList<WorkflowRunItem>> GetWorkflowRunsAsync(string repository, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.github.com/repos/{repository}/actions/runs?per_page={ItemsPerList}");
        Authorize(request);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        (EntityTagHeaderValue ETag, IReadOnlyList<WorkflowRunItem> Runs) cached;
        bool hasCached;
        lock (_runsCache)
        {
            hasCached = _runsCache.TryGetValue(repository, out cached);
        }

        if (hasCached)
        {
            request.Headers.IfNoneMatch.Add(cached.ETag);
        }

        using var response = await GitHubHttp.Client.SendAsync(request, cancellationToken);

        // A 304 doesn't count against the rate limit.
        if (response.StatusCode == HttpStatusCode.NotModified && hasCached)
        {
            return cached.Runs;
        }

        // Actions turned off, or no access to them.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        await ThrowIfFailedAsync(response, cancellationToken);
        using var json = await GitHubHttp.ReadJsonAsync(response, cancellationToken);

        var runs = new List<WorkflowRunItem>();
        foreach (var run in json.RootElement.GetProperty("workflow_runs").EnumerateArray())
        {
            runs.Add(new WorkflowRunItem(
                GitHubHttp.GetString(run, "display_title"),
                GitHubHttp.GetString(run, "name"),
                GitHubHttp.GetString(run, "head_branch"),
                ReadRunState(GitHubHttp.GetString(run, "status"), GitHubHttp.GetString(run, "conclusion")),
                GitHubHttp.GetString(run, "html_url"),
                GitHubHttp.GetTime(run, "updated_at")));
        }

        if (response.Headers.ETag is { } etag)
        {
            lock (_runsCache)
            {
                _runsCache[repository] = (etag, runs);
            }
        }

        return runs;
    }

    private async Task<JsonDocument> PostGraphQlAsync(string query, JsonObject variables, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["query"] = query, ["variables"] = variables };
        using var request = new HttpRequestMessage(HttpMethod.Post, GraphQlUrl)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        Authorize(request);

        using var response = await GitHubHttp.Client.SendAsync(request, cancellationToken);
        await ThrowIfFailedAsync(response, cancellationToken);

        var json = await GitHubHttp.ReadJsonAsync(response, cancellationToken);
        var root = json.RootElement;
        bool hasData = root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object;
        if (!hasData && root.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
        {
            var first = errors[0];
            json.Dispose();
            if (GitHubHttp.GetString(first, "type") == "RATE_LIMITED")
            {
                throw new GitHubRateLimitException(DateTimeOffset.UtcNow.AddMinutes(1));
            }

            throw new GitHubException(GitHubHttp.GetString(first, "message"));
        }

        if (!hasData)
        {
            json.Dispose();
            throw new GitHubException("GitHub returned an empty response.");
        }

        return json;
    }

    private void Authorize(HttpRequestMessage request) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    /// <summary>
    /// Asks GitHub whether the token still works, using the cheapest authenticated call. Returns false only when
    /// GitHub rejects the token; other failures throw, since they don't say anything about the token.
    /// </summary>
    public async Task<bool> IsTokenValidAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
        Authorize(request);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await GitHubHttp.Client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return false;
        }

        await ThrowIfFailedAsync(response, cancellationToken);
        return true;
    }

    private static async Task ThrowIfFailedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Keep GitHub's explanation and request ID: a rejected token is otherwise impossible to diagnose.
            string requestId = response.Headers.TryGetValues("x-github-request-id", out var ids) ? ids.FirstOrDefault() ?? "none" : "none";
            throw new GitHubUnauthorizedException(await ReadErrorMessageAsync(response, cancellationToken), requestId);
        }

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
            && response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining)
            && remaining.FirstOrDefault() == "0")
        {
            long resetSeconds = response.Headers.TryGetValues("x-ratelimit-reset", out var reset)
                && long.TryParse(reset.FirstOrDefault(), out long parsed)
                    ? parsed
                    : DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds();
            throw new GitHubRateLimitException(DateTimeOffset.FromUnixTimeSeconds(resetSeconds));
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new GitHubException($"GitHub returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
    }

    // GitHub error bodies look like {"message": "Bad credentials", ...}.
    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var json = await GitHubHttp.ReadJsonAsync(response, cancellationToken);
            string message = GitHubHttp.GetString(json.RootElement, "message");
            return message.Length > 0 ? message : $"HTTP {(int)response.StatusCode}";
        }
        catch (JsonException)
        {
            return $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
        }
    }

    private static SearchResult<PullRequestItem> ReadPullRequests(JsonElement search)
    {
        var items = new List<PullRequestItem>();
        foreach (var node in search.GetProperty("nodes").EnumerateArray())
        {
            // Results the token can't read come back as null or empty objects.
            if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty("number", out var number))
            {
                continue;
            }

            string author = node.TryGetProperty("author", out var authorNode) ? GitHubHttp.GetString(authorNode, "login") : string.Empty;
            items.Add(new PullRequestItem(
                number.GetInt32(),
                GitHubHttp.GetString(node, "title"),
                GitHubHttp.GetString(node, "url"),
                GitHubHttp.GetString(node.GetProperty("repository"), "nameWithOwner"),
                author.Length > 0 ? author : null,
                node.TryGetProperty("isDraft", out var draft) && draft.ValueKind == JsonValueKind.True,
                ReadCheckState(node),
                GitHubHttp.GetString(node, "reviewDecision") switch
                {
                    "APPROVED" => ReviewDecision.Approved,
                    "CHANGES_REQUESTED" => ReviewDecision.ChangesRequested,
                    "REVIEW_REQUIRED" => ReviewDecision.ReviewRequired,
                    _ => ReviewDecision.None,
                },
                GitHubHttp.GetTime(node, "updatedAt")));
        }

        return new SearchResult<PullRequestItem>(search.GetProperty("issueCount").GetInt32(), items);
    }

    private static SearchResult<IssueItem> ReadIssues(JsonElement search)
    {
        var items = new List<IssueItem>();
        foreach (var node in search.GetProperty("nodes").EnumerateArray())
        {
            if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty("number", out var number))
            {
                continue;
            }

            items.Add(new IssueItem(
                number.GetInt32(),
                GitHubHttp.GetString(node, "title"),
                GitHubHttp.GetString(node, "url"),
                GitHubHttp.GetString(node.GetProperty("repository"), "nameWithOwner")));
        }

        return new SearchResult<IssueItem>(search.GetProperty("issueCount").GetInt32(), items);
    }

    // commits(last: 1).nodes[0].commit.statusCheckRollup.state
    private static CheckState ReadCheckState(JsonElement pullRequest)
    {
        if (!pullRequest.TryGetProperty("commits", out var commits)
            || !commits.TryGetProperty("nodes", out var nodes)
            || nodes.GetArrayLength() == 0
            || !nodes[0].TryGetProperty("commit", out var commit)
            || !commit.TryGetProperty("statusCheckRollup", out var rollup))
        {
            return CheckState.None;
        }

        return GitHubHttp.GetString(rollup, "state") switch
        {
            "SUCCESS" => CheckState.Success,
            "FAILURE" or "ERROR" => CheckState.Failure,
            "PENDING" or "EXPECTED" => CheckState.Pending,
            _ => CheckState.None,
        };
    }

    private static RunState ReadRunState(string status, string conclusion) => status switch
    {
        "completed" => conclusion switch
        {
            "success" => RunState.Succeeded,
            "failure" or "timed_out" or "startup_failure" => RunState.Failed,
            "cancelled" => RunState.Canceled,
            _ => RunState.Other,
        },
        "in_progress" => RunState.InProgress,
        "queued" or "waiting" or "requested" or "pending" => RunState.Queued,
        _ => RunState.Other,
    };
}
