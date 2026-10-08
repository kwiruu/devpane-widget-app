using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevPane.Integrations.Jira;
using DevPane.Widgets.Jira;
using Microsoft.Windows.Widgets;

namespace DevPane.Widgets.Cards;

/// <summary>
/// Jira issues: your open issues by default, or the current sprint, issues you reported, or issues you watch, for all
/// projects or one. Small shows the count and the top issue; medium and large add counts by status and issue rows.
/// </summary>
internal sealed class JiraCard : PollingCard<JiraCard.Reading>
{
    public const string DefinitionId = "Jira";

    private const string CustomizeTemplate = "JiraCustomize";
    private const string AllProjects = "all";
    private const string ApiTokensUrl = "https://id.atlassian.com/manage-profile/security/api-tokens";

    // Issues fetched per refresh, for the counts; rows shown per size. Content taller than the card makes the Widgets
    // Board draw nothing.
    private const int FetchedIssues = 100;
    private const int MediumRows = 3;
    private const int LargeRows = 6;

    // Tile sizes. Keep in sync with the template's tile end columns (8px wide).
    private const int TileEndWidth = 8;
    private const int TileHeight = 52;

    // Issues due within this many days count as due soon.
    private const int DueSoonDays = 7;

    // Opening the board again within this window reuses the last result instead of calling Jira.
    private static readonly TimeSpan MinimumRefreshAge = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private Filter _filter;
    private volatile bool _refreshRequested;
    private bool _customizing;
    private IReadOnlyList<JiraProject>? _projects;

    public JiraCard(string id, WidgetSize size, string? customState)
        : base(id, size, customState)
    {
        _filter = Filter.Read(customState);
        JiraSession.Changed += OnSessionChanged;
    }

    internal enum View
    {
        Assigned,
        Sprint,
        Reported,
        Watching,
    }

    /// <param name="ProjectKey">One project's key, or null for all projects.</param>
    internal sealed record Filter(View View, string? ProjectKey)
    {
        public static Filter Default { get; } = new(View.Assigned, null);

        public string Jql
        {
            get
            {
                string condition = View switch
                {
                    View.Sprint => "sprint in openSprints() AND assignee = currentUser()",
                    View.Reported => "reporter = currentUser() AND statusCategory != Done",
                    View.Watching => "watcher = currentUser() AND statusCategory != Done",
                    _ => "assignee = currentUser() AND statusCategory != Done",
                };

                string project = ProjectKey is null ? string.Empty : $" AND project = \"{ProjectKey.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
                return $"{condition}{project} ORDER BY priority DESC, updated DESC";
            }
        }

        public string Label => (View switch
        {
            View.Sprint => "Current sprint",
            View.Reported => "Reported by you",
            View.Watching => "Watching",
            _ => "Assigned to you",
        }) + (ProjectKey is null ? string.Empty : $" · {ProjectKey}");

        // Custom state is saved by Windows with the card: {"view":"sprint","project":"DEV"}, or empty for the default.
        public static Filter Read(string? customState)
        {
            if (string.IsNullOrWhiteSpace(customState))
            {
                return Default;
            }

            try
            {
                return JsonNode.Parse(customState) is JsonObject state
                    ? new Filter(
                        Enum.TryParse<View>(state["view"]?.GetValue<string>(), ignoreCase: true, out var view) ? view : View.Assigned,
                        state["project"]?.GetValue<string>())
                    : Default;
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException)
            {
                return Default;
            }
        }

        public string Write() => this == Default
            ? string.Empty
            : new JsonObject { ["view"] = View.ToString().ToLowerInvariant(), ["project"] = ProjectKey }.ToJsonString();
    }

    /// <param name="HasMore">True when more issues matched than were fetched, so the counts are lower bounds.</param>
    /// <param name="Problem">Why the last refresh failed, if it did.</param>
    internal sealed record Reading(
        Filter Filter,
        IReadOnlyList<JiraIssue> Issues,
        bool HasMore,
        DateTimeOffset? UpdatedAt,
        string? Problem);

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

    protected override TimeSpan Interval => TimeSpan.FromSeconds(90);

    // Updating a card redraws it and clears what's typed in its inputs, so the connect screen isn't updated on a timer.
    protected override bool PushAfterSample => JiraSession.View.State == JiraConnectionState.Connected;

    public override void Dispose()
    {
        JiraSession.Changed -= OnSessionChanged;
        base.Dispose();
    }

    public override void OnAction(string verb, string data)
    {
        switch (verb)
        {
            case "connect":
                var inputs = CardInputs.Read(data);
                _ = JiraSession.ConnectAsync(
                    inputs.GetValueOrDefault("site") ?? string.Empty,
                    inputs.GetValueOrDefault("email") ?? string.Empty,
                    inputs.GetValueOrDefault("token") ?? string.Empty);
                break;
            case "refresh":
                _refreshRequested = true;
                RefreshNow();
                break;
            case "saveCustomization":
                SaveCustomization(CardInputs.Read(data));
                break;
            case "cancelCustomization":
                ExitCustomization();
                break;
            case "disconnect":
                JiraSession.Disconnect();
                ExitCustomization();
                break;
        }
    }

    public override void OnCustomizationRequested()
    {
        lock (_gate)
        {
            _customizing = true;
            _projects = null;
        }

        Push(includeTemplate: true);
        _ = LoadProjectsAsync();
    }

    protected override async ValueTask<Reading> TakeSampleAsync()
    {
        Filter filter;
        lock (_gate)
        {
            filter = _filter;
        }

        var latest = Latest is { } previous && previous.Filter == filter ? previous : null;
        if (JiraSession.Client is not { } client)
        {
            return new Reading(filter, [], false, null, null);
        }

        bool requested = _refreshRequested;
        _refreshRequested = false;
        if (!requested && latest?.UpdatedAt is { } updatedAt && DateTimeOffset.Now - updatedAt < MinimumRefreshAge)
        {
            return latest;
        }

        try
        {
            var result = await client.SearchAsync(filter.Jql, FetchedIssues, CancellationToken.None);
            return new Reading(filter, result.Issues, result.HasMore, DateTimeOffset.Now, null);
        }
        catch (JiraUnauthorizedException)
        {
            JiraSession.HandleUnauthorized(client);
            return new Reading(filter, [], false, null, null);
        }
        catch (Exception e) when (DescribeFailure(e, filter, latest is not null) is { } problem)
        {
            Log.Error("Jira refresh failed", e);
            return (latest ?? new Reading(filter, [], false, null, null)) with { Problem = problem };
        }
    }

    protected override JsonObject Describe(Reading? reading)
    {
        lock (_gate)
        {
            if (_customizing)
            {
                return DescribeCustomization();
            }
        }

        var session = JiraSession.View;
        bool light = WindowsTheme.IsLight();
        var data = new JsonObject
        {
            ["state"] = session.State switch
            {
                JiraConnectionState.Connected => "connected",
                JiraConnectionState.Checking => "checking",
                _ => "disconnected",
            },
            ["notice"] = session.Notice ?? string.Empty,
            ["tokensUrl"] = ApiTokensUrl,
            ["mark"] = JiraStyle.Mark,
            // The large connect screen is centered in this much height; medium has no room to center.
            ["centerHeight"] = Size == WidgetSize.Large ? "360px" : "0px",
        };

        var (buttonLeft, buttonFill, buttonRight) = JiraStyle.Button(light);
        data["buttonLeft"] = buttonLeft;
        data["buttonFill"] = buttonFill;
        data["buttonRight"] = buttonRight;
        data["buttonText"] = JiraStyle.ButtonTextColor(light);

        if (session.State != JiraConnectionState.Connected || JiraSession.Client is not { } client)
        {
            return data;
        }

        Filter filter;
        lock (_gate)
        {
            filter = _filter;
        }

        var current = reading?.Filter == filter ? reading : null;
        var issues = current?.Issues ?? [];
        var today = DateOnly.FromDateTime(DateTime.Now);
        var culture = CultureInfo.CurrentCulture;
        string more = current?.HasMore == true ? "+" : string.Empty;

        // Small: how many, and the top issue.
        data["count"] = current?.UpdatedAt is null ? Format.Missing : $"{issues.Count.ToString(culture)}{more}";
        data["countLabel"] = filter.View switch
        {
            View.Sprint => "in your current sprint",
            View.Reported => "open issues you reported",
            View.Watching => "open issues you watch",
            _ => issues.Count == 1 ? "open issue assigned to you" : "open issues assigned to you",
        };

        var open = issues.Where(issue => issue.Category != JiraStatusCategory.Done).ToList();
        bool IsDueSoon(JiraIssue issue) => issue.Category != JiraStatusCategory.Done && issue.DueDate is { } due && due <= today.AddDays(DueSoonDays);
        bool IsOverdue(JiraIssue issue) => issue.Category != JiraStatusCategory.Done && issue.DueDate is { } due && due < today;
        bool hasOverdue = issues.Any(IsOverdue);

        data["tiles"] = new JsonArray
        {
            Tile("To do", issues.Count(issue => issue.Category == JiraStatusCategory.ToDo), more, LabelTone.Neutral,
                client.SearchUrl(filter.Jql), light),
            Tile("In progress", issues.Count(issue => issue.Category == JiraStatusCategory.InProgress), more, LabelTone.Accent,
                client.SearchUrl(filter.Jql), light),
            // Once anything is overdue the tile counts only what's overdue, so its number matches its label.
            // Due soon includes overdue issues, so counting that under an "Overdue" label would overstate it.
            hasOverdue
                ? Tile("Overdue", issues.Count(IsOverdue), more, LabelTone.Danger, client.SearchUrl(filter.Jql), light)
                : Tile("Due soon", issues.Count(IsDueSoon), more, LabelTone.Attention, client.SearchUrl(filter.Jql), light),
        };

        int rows = Size switch
        {
            WidgetSize.Small => 1,
            WidgetSize.Medium => MediumRows,
            _ => LargeRows,
        };

        // Open issues first, in Jira's order: by priority, then most recently updated.
        var shown = open.Concat(issues.Where(issue => issue.Category == JiraStatusCategory.Done)).Take(rows).ToList();
        data["hasIssues"] = shown.Count > 0;
        data["issues"] = new JsonArray(shown
            .Select((issue, index) => (JsonNode)IssueRow(issue, client, IsOverdue(issue), first: index == 0, light, today))
            .ToArray());
        data["empty"] = current?.UpdatedAt is null ? "Loading from Jira…" : "Nothing here. Nice work.";

        string freshness = (current?.UpdatedAt, current?.Problem) switch
        {
            (_, { } problem) => problem,
            ({ } updatedAt, _) => $"Updated {Format.RelativeTime(updatedAt)}",
            _ => "Loading from Jira…",
        };
        data["footer"] = $"{filter.Label} · {freshness}";
        data["syncIcon"] = Octicons.Image(Octicon.Sync, CardStyle.ToneColor(LabelTone.Neutral, light));
        data["searchUrl"] = client.SearchUrl(filter.Jql);
        return data;
    }

    private static JsonObject Tile(string label, int count, string more, LabelTone activeTone, string url, bool light)
    {
        var tone = count > 0 ? activeTone : LabelTone.Neutral;
        var (left, fill, right) = CardStyle.Surface(tone, light, TileEndWidth, TileHeight);
        return new JsonObject
        {
            ["label"] = label,
            ["value"] = $"{count.ToString(CultureInfo.CurrentCulture)}{(count > 0 ? more : string.Empty)}",
            ["url"] = url,
            ["height"] = $"{TileHeight}px",
            ["left"] = left,
            ["fill"] = fill,
            ["right"] = right,
        };
    }

    // "DEV-142 · Web App · Due Sep 20", with the status as a lozenge and the priority as an arrow.
    private static JsonObject IssueRow(JiraIssue issue, JiraClient client, bool overdue, bool first, bool light, DateOnly today)
    {
        var (left, fill, right) = JiraStyle.Lozenge(issue.Category, light);
        var culture = CultureInfo.CurrentCulture;
        string due = issue.DueDate switch
        {
            null => string.Empty,
            { } date when date == today => "Due today",
            { } date when overdue => $"Overdue {date.ToString("MMM d", culture)}",
            { } date => $"Due {date.ToString("MMM d", culture)}",
        };

        return new JsonObject
        {
            ["key"] = issue.Key,
            ["summary"] = issue.Summary,
            ["url"] = client.IssueUrl(issue.Key),
            ["typeIcon"] = JiraStyle.IssueTypeIcon(issue),
            ["priorityIcon"] = JiraStyle.PriorityIcon(issue.Priority),
            ["hasPriority"] = issue.Priority is not null && JiraStyle.PriorityIcon(issue.Priority).Length > 0,
            ["project"] = issue.ProjectName,
            ["due"] = due,
            ["dueColor"] = overdue ? "attention" : "default",
            ["dueSubtle"] = !overdue,
            ["status"] = issue.Status.ToUpperInvariant(),
            ["statusColor"] = JiraStyle.LozengeTextColor(issue.Category),
            ["statusLeft"] = left,
            ["statusFill"] = fill,
            ["statusRight"] = right,
            ["spacing"] = first ? "none" : "default",
        };
    }

    private static string? DescribeFailure(Exception exception, Filter filter, bool hasEarlierData) => exception switch
    {
        JiraRateLimitException rateLimit =>
            $"Jira's rate limit was reached. Trying again at {rateLimit.RetryAt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)}.",
        JiraRequestException when filter.View == View.Sprint => "This Jira site has no sprints. Pick another view in Customize.",
        JiraRequestException request => request.Message,
        JiraException or HttpRequestException or TaskCanceledException or JsonException => hasEarlierData
            ? "Couldn't reach Jira. Showing the last update."
            : "Couldn't reach Jira. Trying again in a minute.",
        _ => null,
    };

    private void OnSessionChanged()
    {
        try
        {
            lock (_gate)
            {
                if (_customizing)
                {
                    return;
                }
            }

            Push(includeTemplate: false);
            if (JiraSession.View.State == JiraConnectionState.Connected)
            {
                _refreshRequested = true;
                RefreshNow();
            }
        }
        catch (Exception e)
        {
            Log.Error("Jira card update after a connection change failed", e);
        }
    }

    // Customization: which issues, and optionally one project.

    private JsonObject DescribeCustomization()
    {
        var projects = new JsonArray();
        foreach (var project in _projects ?? [])
        {
            projects.Add(new JsonObject { ["value"] = project.Key, ["title"] = $"{project.Name} ({project.Key})" });
        }

        var session = JiraSession.View;
        return new JsonObject
        {
            ["view"] = _filter.View.ToString().ToLowerInvariant(),
            ["project"] = _filter.ProjectKey ?? AllProjects,
            ["projects"] = projects,
            ["loading"] = _projects is null,
            ["connected"] = session.State == JiraConnectionState.Connected,
            ["site"] = session.Site ?? string.Empty,
        };
    }

    private async Task LoadProjectsAsync()
    {
        IReadOnlyList<JiraProject> projects = [];
        if (JiraSession.Client is { } client)
        {
            try
            {
                projects = await client.GetProjectsAsync(CancellationToken.None);
            }
            catch (Exception e) when (e is JiraException or HttpRequestException or TaskCanceledException or JsonException)
            {
                Log.Info($"Listing Jira projects failed: {e.Message}");
            }
        }

        bool stillCustomizing;
        lock (_gate)
        {
            _projects = projects;
            stillCustomizing = _customizing;
        }

        if (stillCustomizing)
        {
            Push(includeTemplate: false);
        }
    }

    private void SaveCustomization(Dictionary<string, string> inputs)
    {
        Filter filter;
        lock (_gate)
        {
            var view = Enum.TryParse<View>(inputs.GetValueOrDefault("view"), ignoreCase: true, out var chosen) ? chosen : _filter.View;
            string? project = inputs.GetValueOrDefault("project") is { Length: > 0 } key && key != AllProjects ? key : null;
            filter = new Filter(view, project);
            _filter = filter;
            CustomState = filter.Write();
        }

        Log.Info($"Jira card {Id} now shows {filter.Label}");
        ExitCustomization();
        _refreshRequested = true;
        RefreshNow();
    }

    private void ExitCustomization()
    {
        lock (_gate)
        {
            _customizing = false;
        }

        Push(includeTemplate: true);
    }
}
