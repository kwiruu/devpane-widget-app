using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DevPane.Integrations.GitHub;
using DevPane.Widgets.GitHub;
using Microsoft.Windows.Widgets;
using Microsoft.Windows.Widgets.Providers;

namespace DevPane.Widgets.Cards;

/// <summary>
/// Pull requests waiting for review, the user's own pull requests with CI status, assigned issues, and (for a
/// single repository) workflow runs. Customize picks all repositories or one repository.
/// </summary>
internal sealed partial class GitHubCard : GitHubCardBase<GitHubCard.Reading>
{
    public const string DefinitionId = "GitHub";

    private const string CustomizeTemplate = "GitHubCustomize";
    private const string AllRepositoriesChoice = "all";

    // How many list rows fit, measured from screenshots (about 250px below the header on medium, 410px on large).
    // Content taller than the card makes the Widgets Board draw nothing.
    private const int MediumRows = 2;
    private const int LargeRows = 5;

    // Sizes of the tinted tiles and pills. Keep in sync with the template's end columns (8px for tiles, 10px for
    // pills) and pill columns (20px high).
    private const int TileEndWidth = 8;
    private const int TileHeight = 64;
    private const int SmallTileHeight = 58;
    private const int PillEndWidth = 10;
    private const int PillHeight = 20;

    // Opening the board again within this window reuses the last result instead of calling GitHub.
    private static readonly TimeSpan MinimumRefreshAge = TimeSpan.FromMinutes(1);

    private readonly object _gate = new();
    private string? _repository;
    private bool _customizing;
    private string _customizeError = string.Empty;
    private string _customizeTyped = string.Empty;
    private IReadOnlyList<string> _recentRepositories = [];

    public GitHubCard(WidgetContext context, string? customState)
        : base(context, customState)
    {
        _repository = ReadRepository(customState);
    }

    /// <param name="Repository">The repository this reading covers; null for all repositories.</param>
    /// <param name="Problem">Why the last refresh failed, if it did.</param>
    internal sealed record Reading(string? Repository, GitHubOverview? Overview, DateTimeOffset? UpdatedAt, string? Problem);

    protected override string TemplateName
    {
        get
        {
            lock (_gate)
            {
                return _customizing ? CustomizeTemplate : DefinitionId;
            }
        }
    }

    // GitHub data changes slowly, and each refresh uses API quota.
    protected override TimeSpan Interval => TimeSpan.FromMinutes(3);

    protected override bool IgnoreSessionChanges
    {
        get
        {
            lock (_gate)
            {
                return _customizing;
            }
        }
    }

    public override void OnAction(string verb, string data)
    {
        switch (verb)
        {
            case "saveCustomization":
                _ = SaveCustomizationAsync(data);
                break;
            case "cancelCustomization":
                ExitCustomization(refresh: false);
                break;
            case "signOut":
                GitHubSession.SignOut();
                ExitCustomization(refresh: false);
                break;
            default:
                base.OnAction(verb, data);
                break;
        }
    }

    public override void OnCustomizationRequested()
    {
        lock (_gate)
        {
            _customizing = true;
            _customizeError = string.Empty;
            _customizeTyped = string.Empty;
        }

        Push(includeTemplate: true);
        _ = LoadRecentRepositoriesAsync();
    }

    protected override async ValueTask<Reading> TakeSampleAsync()
    {
        string? repository;
        lock (_gate)
        {
            repository = _repository;
        }

        var latest = Latest is { } previous && previous.Repository == repository ? previous : null;
        if (await GitHubSession.GetClientAsync() is not { } client)
        {
            return new Reading(repository, null, null, null);
        }

        if (!ConsumeRefreshRequest() && latest?.UpdatedAt is { } updatedAt && DateTimeOffset.Now - updatedAt < MinimumRefreshAge)
        {
            return latest;
        }

        try
        {
            var overview = await client.GetOverviewAsync(repository, CancellationToken.None);
            GitHubSession.SetLogin(overview.Login);
            return new Reading(repository, overview, DateTimeOffset.Now, null);
        }
        catch (GitHubUnauthorizedException e)
        {
            if (await GitHubSession.HandleUnauthorizedAsync(client, e))
            {
                return new Reading(repository, null, null, null);
            }

            return (latest ?? new Reading(repository, null, null, null)) with
            {
                Problem = "GitHub turned down this refresh. Trying again in a few minutes.",
            };
        }
        catch (Exception e) when (DescribeFailure(e, latest?.Overview is not null) is { } problem)
        {
            Log.Error("GitHub refresh failed", e);
            return (latest ?? new Reading(repository, null, null, null)) with { Problem = problem };
        }
    }

    protected override JsonObject Describe(Reading? reading)
    {
        string? repository;
        lock (_gate)
        {
            if (_customizing)
            {
                return DescribeCustomization();
            }

            repository = _repository;
        }

        var session = GitHubSession.View;
        var data = DescribeSignIn(session);
        if (session.State != GitHubSignInState.SignedIn)
        {
            return data;
        }

        var current = reading?.Repository == repository ? reading : null;
        var overview = current?.Overview;
        bool light = LightTheme;
        var (prDetail, prDetailTone) = SummarizeMyPullRequests(overview);

        data["tiles"] = new JsonArray
        {
            Tile("Reviews", Octicon.Review, overview?.ReviewRequests.TotalCount, LabelTone.Attention,
                SearchUrl(repository, "pulls", "is:open is:pr review-requested:@me", "https://github.com/pulls/review-requested"),
                "Open review requests on GitHub", badge: null, light),
            Tile("My PRs", Octicon.PullRequest, overview?.MyPullRequests.TotalCount, LabelTone.Success,
                SearchUrl(repository, "pulls", "is:open is:pr author:@me", "https://github.com/pulls"),
                "Open your pull requests on GitHub",
                badge: prDetail.Length > 0 ? (CardStyle.StatusIcon(prDetailTone, light), prDetail) : null, light),
            Tile("Issues", Octicon.Issue, overview?.AssignedIssues.TotalCount, LabelTone.Accent,
                SearchUrl(repository, "issues", "is:open is:issue assignee:@me", "https://github.com/issues/assigned"),
                "Open your assigned issues on GitHub", badge: null, light),
        };
        data["sections"] = Size == WidgetSize.Small || overview is null
            ? new JsonArray()
            : BuildSections(overview, repository is null, light);
        data["avatar"] = overview?.AvatarUrl ?? string.Empty;
        data["syncIcon"] = GitHubStyle.Icon(Octicon.Sync, LabelTone.Neutral, light);
        string freshness = DescribeFreshness(current?.UpdatedAt, current?.Problem);
        string? owner = repository ?? overview?.Login;
        data["footer"] = owner is null ? freshness : $"{owner} · {freshness}";
        return data;
    }

    /// <param name="activeTone">The icon's color while the tile counts anything; empty and loading tiles are gray. The
    /// tile's background stays gray either way.</param>
    /// <param name="badge">A status icon beside the count, with its description, or null for none.</param>
    private JsonObject Tile(
        string label,
        Octicon icon,
        int? count,
        LabelTone activeTone,
        string url,
        string linkTitle,
        (string Icon, string Text)? badge,
        bool light)
    {
        bool small = Size == WidgetSize.Small;
        int height = small ? SmallTileHeight : TileHeight;
        var iconTone = count > 0 ? activeTone : LabelTone.Neutral;
        var (left, fill, right) = CardStyle.Surface(LabelTone.Neutral, light, TileEndWidth, height);
        return new JsonObject
        {
            ["label"] = label,
            ["count"] = count?.ToString(CultureInfo.CurrentCulture) ?? Format.Missing,
            ["valueSize"] = small ? "large" : "extraLarge",
            ["icon"] = GitHubStyle.Icon(icon, iconTone, light),
            ["badge"] = badge?.Icon ?? string.Empty,
            ["badgeText"] = badge?.Text ?? string.Empty,
            ["url"] = url,
            ["linkTitle"] = linkTitle,
            ["height"] = $"{height}px",
            ["left"] = left,
            ["fill"] = fill,
            ["right"] = right,
        };
    }

    private JsonArray BuildSections(GitHubOverview overview, bool allRepositories, bool light)
    {
        var sections = new JsonArray();
        if (Size == WidgetSize.Medium)
        {
            // One list fits: runs for a single repository, otherwise whatever needs the user most.
            if (overview.WorkflowRuns is { } runs)
            {
                sections.Add(WorkflowRunSection(runs, MediumRows, light));
            }
            else if (overview.ReviewRequests.Items.Count > 0 || overview.MyPullRequests.Items.Count == 0)
            {
                sections.Add(ReviewSection(overview, allRepositories, MediumRows, light));
            }
            else
            {
                sections.Add(MyPullRequestSection(overview, allRepositories, MediumRows, light));
            }

            return sections;
        }

        // Large fits three lists, including the user's assigned issues alongside pull requests.
        if (overview.WorkflowRuns is { } repositoryRuns)
        {
            bool reviews = overview.ReviewRequests.Items.Count > 0;
            var rows = SplitRows(
                LargeRows,
                repositoryRuns.Count,
                reviews ? overview.ReviewRequests.Items.Count : overview.MyPullRequests.Items.Count,
                overview.AssignedIssues.Items.Count);
            sections.Add(WorkflowRunSection(repositoryRuns, rows[0], light));
            sections.Add(reviews
                ? ReviewSection(overview, allRepositories, rows[1], light)
                : MyPullRequestSection(overview, allRepositories, rows[1], light));
            sections.Add(IssueSection(overview, allRepositories, rows[2], light));
        }
        else
        {
            var rows = SplitRows(
                LargeRows,
                overview.ReviewRequests.Items.Count,
                overview.MyPullRequests.Items.Count,
                overview.AssignedIssues.Items.Count);
            sections.Add(ReviewSection(overview, allRepositories, rows[0], light));
            sections.Add(MyPullRequestSection(overview, allRepositories, rows[1], light));
            sections.Add(IssueSection(overview, allRepositories, rows[2], light));
        }

        return sections;
    }

    /// <summary>
    /// Shares the large card's rows between several lists. Each list keeps one row, for its first item or its "nothing
    /// here" line; the rest go round-robin to lists that still have unshown items, so a short list leaves its rows to
    /// the others.
    /// </summary>
    private static int[] SplitRows(int totalRows, params int[] itemCounts)
    {
        var rows = new int[itemCounts.Length];
        Array.Fill(rows, 1);

        int remaining = totalRows - rows.Length;
        bool progress = true;
        while (remaining > 0 && progress)
        {
            progress = false;
            for (int i = 0; i < rows.Length && remaining > 0; i++)
            {
                if (rows[i] < itemCounts[i])
                {
                    rows[i]++;
                    remaining--;
                    progress = true;
                }
            }
        }

        return rows;
    }

    private static JsonObject ReviewSection(GitHubOverview overview, bool allRepositories, int count, bool light) => Section(
        Octicon.Review,
        "Waiting for your review",
        overview.ReviewRequests.TotalCount,
        "No reviews waiting for you.",
        overview.ReviewRequests.Items.Take(count).Select(pr => Item(
            GitHubStyle.Icon(Octicon.Dot, LabelTone.Neutral, light),
            pr.Title,
            Details(Where(pr.Repository, pr.Number, allRepositories), pr.Author, pr.UpdatedAt),
            pr.IsDraft ? "Draft" : string.Empty,
            LabelTone.Neutral,
            pr.Url,
            light)),
        light);

    private static JsonObject MyPullRequestSection(GitHubOverview overview, bool allRepositories, int count, bool light) => Section(
        Octicon.PullRequest,
        "Your pull requests",
        overview.MyPullRequests.TotalCount,
        "You have no open pull requests.",
        overview.MyPullRequests.Items.Take(count).Select(pr =>
        {
            var (status, tone) = DescribePullRequestStatus(pr);
            // A passing PR gets a checkmark instead of a "Passing" pill: with everything else about it already good,
            // the check is enough.
            string? statusIcon = status == "Passing" ? GitHubStyle.Icon(Octicon.Check, tone, light) : null;
            return Item(
                GitHubStyle.Icon(Octicon.Dot, LabelTone.Neutral, light),
                pr.Title,
                Details(Where(pr.Repository, pr.Number, allRepositories), author: null, pr.UpdatedAt),
                status,
                tone,
                pr.Url,
                light,
                statusIcon);
        }),
        light);

    private static JsonObject IssueSection(GitHubOverview overview, bool allRepositories, int count, bool light) => Section(
        Octicon.Issue,
        "Your issues",
        overview.AssignedIssues.TotalCount,
        "You have no open issues assigned.",
        overview.AssignedIssues.Items.Take(count).Select(issue => Item(
            GitHubStyle.Icon(Octicon.Dot, LabelTone.Neutral, light),
            issue.Title,
            Details(Where(issue.Repository, issue.Number, allRepositories), author: null, issue.UpdatedAt),
            status: string.Empty,
            LabelTone.Neutral,
            issue.Url,
            light)),
        light);

    private static JsonObject WorkflowRunSection(IReadOnlyList<WorkflowRunItem> runs, int count, bool light) => Section(
        Octicon.Workflow,
        "Workflow runs",
        count: null,
        "No workflow runs yet.",
        runs.Take(count).Select(run =>
        {
            var (status, tone) = run.State switch
            {
                RunState.Succeeded => ("Passed", LabelTone.Success),
                RunState.Failed => ("Failed", LabelTone.Danger),
                RunState.InProgress => ("Running", LabelTone.Attention),
                RunState.Queued => ("Queued", LabelTone.Attention),
                RunState.Canceled => ("Canceled", LabelTone.Neutral),
                _ => (string.Empty, LabelTone.Neutral),
            };
            string where = run.Branch.Length > 0 ? $"{run.WorkflowName} · {run.Branch}" : run.WorkflowName;
            return Item(
                GitHubStyle.Icon(Octicon.Workflow, LabelTone.Neutral, light),
                run.Title,
                Details(where, author: null, run.UpdatedAt),
                status,
                tone,
                run.Url,
                light);
        }),
        light);

    /// <param name="count">Every match on GitHub, shown in a badge beside the title; null for no badge.</param>
    private static JsonObject Section(
        Octicon icon,
        string title,
        int? count,
        string emptyText,
        IEnumerable<JsonObject> items,
        bool light)
    {
        var array = new JsonArray();
        foreach (var item in items)
        {
            array.Add(item);
        }

        var (left, fill, right) = CardStyle.Surface(LabelTone.Neutral, light, PillEndWidth, PillHeight);
        return new JsonObject
        {
            ["icon"] = GitHubStyle.Icon(icon, LabelTone.Neutral, light),
            ["title"] = title,
            ["count"] = count?.ToString(CultureInfo.CurrentCulture) ?? string.Empty,
            ["countLeft"] = left,
            ["countFill"] = fill,
            ["countRight"] = right,
            ["empty"] = emptyText,
            ["hasItems"] = array.Count > 0,
            ["items"] = array,
        };
    }

    /// <param name="statusIcon">Shown beside the title instead of the status pill, for example a checkmark for "Passing".</param>
    private static JsonObject Item(
        string icon,
        string title,
        string details,
        string status,
        LabelTone tone,
        string url,
        bool light,
        string? statusIcon = null)
    {
        var (left, fill, right) = CardStyle.Surface(tone, light, PillEndWidth, PillHeight);
        return new JsonObject
        {
            ["icon"] = icon,
            ["title"] = title,
            ["details"] = details,
            ["url"] = url,
            ["status"] = status,
            ["statusColor"] = CardStyle.TextColor(tone),
            ["statusLeft"] = left,
            ["statusFill"] = fill,
            ["statusRight"] = right,
            ["hasStatusIcon"] = statusIcon is not null,
            ["statusIcon"] = statusIcon ?? string.Empty,
        };
    }

    private static string Where(string repository, int number, bool includeRepository) =>
        includeRepository ? $"{repository} #{number}" : $"#{number}";

    // "owner/repo #3 · octocat · 2d": where the item is, who opened it, and how long since it changed.
    private static string Details(string where, string? author, DateTimeOffset? updatedAt)
    {
        string details = author is null ? where : $"{where} · {author}";
        return updatedAt is { } time ? $"{details} · {Format.Age(time)}" : details;
    }

    // Most urgent first: failing checks, requested changes, then approval and check progress.
    private static (string Status, LabelTone Tone) DescribePullRequestStatus(PullRequestItem pr) => pr switch
    {
        { IsDraft: true } => ("Draft", LabelTone.Neutral),
        { Checks: CheckState.Failure } => ("Failing", LabelTone.Danger),
        { Review: ReviewDecision.ChangesRequested } => ("Needs changes", LabelTone.Danger),
        { Checks: CheckState.Pending } => ("Running", LabelTone.Attention),
        { Review: ReviewDecision.Approved } => ("Approved", LabelTone.Success),
        { Checks: CheckState.Success } => ("Passing", LabelTone.Success),
        _ => (string.Empty, LabelTone.Neutral),
    };

    private static (string Detail, LabelTone Tone) SummarizeMyPullRequests(GitHubOverview? overview)
    {
        var items = overview?.MyPullRequests.Items ?? [];
        int failing = items.Count(pr => pr.Checks == CheckState.Failure);
        int running = items.Count(pr => pr.Checks == CheckState.Pending);

        if (failing > 0)
        {
            return ($"{failing} failing", LabelTone.Danger);
        }

        if (running > 0)
        {
            return ($"{running} running", LabelTone.Attention);
        }

        return items.Count > 0 && items.All(pr => pr.Checks == CheckState.Success)
            ? ("checks passing", LabelTone.Success)
            : (string.Empty, LabelTone.Neutral);
    }

    private static string SearchUrl(string? repository, string kind, string query, string allRepositoriesUrl) =>
        repository is null ? allRepositoriesUrl : $"https://github.com/{repository}/{kind}?q={Uri.EscapeDataString(query)}";

    // Customization: pick "All repositories", a recent repository, or type owner/name.

    private JsonObject DescribeCustomization()
    {
        string? listed = _repository is null
            ? null
            : _recentRepositories.FirstOrDefault(name => name.Equals(_repository, StringComparison.OrdinalIgnoreCase));

        var repositories = new JsonArray();
        foreach (string name in _recentRepositories)
        {
            repositories.Add(new JsonObject { ["name"] = name });
        }

        var session = GitHubSession.View;
        return new JsonObject
        {
            ["selected"] = listed ?? AllRepositoriesChoice,
            ["typed"] = _customizeError.Length > 0 ? _customizeTyped : (listed is null ? _repository ?? string.Empty : string.Empty),
            ["repos"] = repositories,
            ["error"] = _customizeError,
            ["login"] = session.State == GitHubSignInState.SignedIn ? session.Login ?? "your GitHub account" : string.Empty,
        };
    }

    private async Task LoadRecentRepositoriesAsync()
    {
        if (await GitHubSession.GetClientAsync() is not { } client)
        {
            return;
        }

        try
        {
            var repositories = await client.GetRecentRepositoriesAsync(CancellationToken.None);
            bool stillCustomizing;
            lock (_gate)
            {
                _recentRepositories = repositories;
                stillCustomizing = _customizing;
            }

            if (stillCustomizing)
            {
                Push(includeTemplate: false);
            }
        }
        catch (Exception e) when (e is GitHubException or HttpRequestException or TaskCanceledException)
        {
            Log.Error("Loading recent repositories failed", e);
        }
    }

    private async Task SaveCustomizationAsync(string data)
    {
        var inputs = CardInputs.Read(data);
        string typed = inputs.GetValueOrDefault("typedRepo")?.Trim() ?? string.Empty;
        string selected = inputs.GetValueOrDefault("repo") ?? AllRepositoriesChoice;

        string? repository;
        if (typed.Length > 0)
        {
            repository = NormalizeRepository(typed);
            if (repository is null)
            {
                ShowCustomizationError(typed, "Enter a repository as owner/name, like microsoft/vscode.");
                return;
            }
        }
        else
        {
            repository = selected == AllRepositoriesChoice ? null : selected;
        }

        if (repository is not null && await GitHubSession.GetClientAsync() is { } client)
        {
            try
            {
                string? found = await client.FindRepositoryAsync(repository, CancellationToken.None);
                if (found is null)
                {
                    ShowCustomizationError(typed, $"Couldn't find {repository}, or your GitHub account can't see it.");
                    return;
                }

                repository = found;
            }
            catch (Exception e) when (e is GitHubException or HttpRequestException or TaskCanceledException)
            {
                Log.Error("Checking repository failed", e);
                ShowCustomizationError(typed, "Couldn't reach GitHub to check that repository. Try again.");
                return;
            }
        }

        lock (_gate)
        {
            _repository = repository;
            CustomState = WriteRepository(repository);
        }

        Log.Info($"GitHub card {Id} now shows {repository ?? "all repositories"}");
        ExitCustomization(refresh: true);
    }

    private void ShowCustomizationError(string typed, string message)
    {
        lock (_gate)
        {
            _customizeTyped = typed;
            _customizeError = message;
        }

        Push(includeTemplate: false);
    }

    private void ExitCustomization(bool refresh)
    {
        lock (_gate)
        {
            _customizing = false;
            _customizeError = string.Empty;
            _customizeTyped = string.Empty;
        }

        Push(includeTemplate: true);
        if (refresh)
        {
            RequestRefresh();
        }
    }

    /// <summary>Accepts "owner/name", "owner/name.git", or a github.com URL; returns "owner/name" or null.</summary>
    private static string? NormalizeRepository(string input)
    {
        string value = input.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            value = string.Join('/', uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Take(2));
        }

        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^4];
        }

        return RepositoryName().IsMatch(value) ? value : null;
    }

    // Custom state is saved by Windows with the card: {"repository":"owner/name"}, or empty for all repositories.
    private static string? ReadRepository(string? customState)
    {
        if (string.IsNullOrWhiteSpace(customState))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(customState)?["repository"]?.GetValue<string>() is { Length: > 0 } repository
                && RepositoryName().IsMatch(repository)
                    ? repository
                    : null;
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string WriteRepository(string? repository) =>
        repository is null ? string.Empty : new JsonObject { ["repository"] = repository }.ToJsonString();

    [GeneratedRegex("^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$")]
    private static partial Regex RepositoryName();
}
