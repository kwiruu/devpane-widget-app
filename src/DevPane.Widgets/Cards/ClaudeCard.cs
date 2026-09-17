using System.Globalization;
using System.Text.Json.Nodes;
using DevPane.Integrations.Claude;
using Microsoft.Windows.Widgets;
using Microsoft.Windows.Widgets.Providers;

namespace DevPane.Widgets.Cards;

/// <summary>
/// Claude Code usage on this PC: the current five-hour session and the week, as percent used when Claude reports it
/// (through Claude Code's status line) and as usage at API prices otherwise. Medium adds the last seven days, and large
/// adds the top projects today.
/// </summary>
internal sealed class ClaudeCard : PollingCard<ClaudeUsageSummary>
{
    public const string DefinitionId = "ClaudeUsage";

    // Claude's orange, for progress bars and the chart.
    private const string ClaudeOrange = "#D97757";

    // Chart image height at its 280px drawn width; stretched to the card's width, it shows about 42px tall.
    // Content taller than the card makes the Widgets Board draw nothing, so this is sized with the rest of the layout.
    private const int ChartHeight = 44;

    private const int MaxProjects = 2;

    // A percentage older than this shows when Claude reported it.
    private static readonly TimeSpan StalePercentAge = TimeSpan.FromMinutes(15);

    // How long setup messages stay on the card.
    private static readonly TimeSpan SetupMessageDuration = TimeSpan.FromMinutes(10);

    private readonly object _gate = new();
    private string? _setupMessage;
    private DateTimeOffset _setupMessageAt;

    public ClaudeCard(WidgetContext context, string? customState)
        : base(context, customState)
    {
    }

    protected override string TemplateName => DefinitionId;

    // Only lines added since the last read are parsed, so frequent refreshes are cheap.
    protected override TimeSpan Interval => TimeSpan.FromSeconds(30);

    public override void OnAction(string verb, string data)
    {
        switch (verb)
        {
            case "refresh":
                RefreshNow();
                break;
            case "showLiveLimits":
                ShowLiveLimits();
                break;
        }
    }

    protected override ValueTask<ClaudeUsageSummary> TakeSampleAsync() => new(Task.Run(ClaudeCodeLogs.Shared.Refresh));

    protected override JsonObject Describe(ClaudeUsageSummary? usage)
    {
        var data = new JsonObject
        {
            ["state"] = usage switch
            {
                null => "loading",
                { LogsFound: false } => "missing",
                _ => "ready",
            },
        };

        if (usage is not { LogsFound: true })
        {
            return data;
        }

        bool light = WindowsTheme.IsLight();
        var now = DateTimeOffset.Now;
        var culture = CultureInfo.CurrentCulture;

        // Models without a known price add no cost, so fall back to tokens when nothing has a price.
        bool byCost = usage.LastSevenDays.Any(day => day.Cost > 0) || !usage.LastSevenDays.Any(day => day.Tokens > 0);
        string Amount(decimal cost, long tokens) => byCost ? Format.Dollars(cost) : Format.TokenCount(tokens);

        data["limits"] = new JsonArray
        {
            Limit("Current session", usage.Session, isWeek: false, first: true, Amount, now, light),
            Limit(usage.Week.ResetsAt is null ? "Last 7 days" : "This week", usage.Week, isWeek: true, first: false, Amount, now, light),
        };

        DescribeFooter(data, usage, Amount(usage.Today.Cost, usage.Today.Tokens), now, light);

        if (Size == WidgetSize.Small)
        {
            return data;
        }

        // Medium has less height, so its sections sit closer together.
        data["gap"] = Size == WidgetSize.Large ? "large" : "medium";

        var days = usage.LastSevenDays;
        data["chartTitle"] = byCost ? "Last 7 days at API prices" : "Last 7 days, tokens";
        data["chartTotal"] = Amount(days.Sum(day => day.Cost), days.Sum(day => day.Tokens));
        data["chart"] = UsageCharts.Bars(
            days.Select(day => byCost ? (double)day.Cost : day.Tokens).ToList(), ClaudeOrange, light, ChartHeight);
        data["days"] = new JsonArray(days
            .Select((day, index) => (JsonNode)new JsonObject
            {
                ["name"] = culture.DateTimeFormat.GetShortestDayName(day.Date.DayOfWeek),
                ["weight"] = index == days.Count - 1 ? "bolder" : "default",
                ["subtle"] = index != days.Count - 1,
            })
            .ToArray());

        if (Size != WidgetSize.Large)
        {
            return data;
        }

        var projects = usage.ProjectsToday.Take(MaxProjects).ToList();
        data["hasProjects"] = projects.Count > 0;
        data["projects"] = new JsonArray(projects
            .Select(project => (JsonNode)new JsonObject
            {
                ["name"] = project.Name,
                ["amount"] = Amount(project.Cost, project.Tokens),
            })
            .ToArray());

        return data;
    }

    /// <summary>
    /// One limit. Large shows it like Claude's usage page: a row with percent used, a full-width progress bar, and when it
    /// resets. Small and medium show the two limits side by side, each with a big number. Without a percentage from
    /// Claude, the number is usage at API prices and there's no bar.
    /// </summary>
    private static JsonObject Limit(
        string title,
        ClaudeUsagePeriod? period,
        bool isWeek,
        bool first,
        Func<decimal, long, string> amount,
        DateTimeOffset now,
        bool light)
    {
        var culture = CultureInfo.CurrentCulture;
        int? percent = period?.Percent;
        LabelTone? warning = percent switch
        {
            >= 90 => LabelTone.Danger,
            >= 75 => LabelTone.Attention,
            _ => null,
        };

        string approximately = period?.ResetIsEstimate == true ? "~" : string.Empty;
        string shortCaption = period switch
        {
            null => "Not started",
            { ResetsAt: { } resetsAt } when isWeek =>
                $"Resets {approximately}{resetsAt.ToString("ddd", culture)} {resetsAt.ToString("t", culture)}",
            { ResetsAt: { } resetsAt } => $"Resets {approximately}{resetsAt.ToString("t", culture)}",
            _ => "On this PC",
        };

        return new JsonObject
        {
            ["title"] = title,
            ["value"] = period switch
            {
                null => Format.Missing,
                { Percent: { } used } => $"{used.ToString(culture)}% used",
                _ => amount(period.Cost, period.Tokens),
            },
            ["bigValue"] = period switch
            {
                null => Format.Missing,
                { Percent: { } used } => $"{used.ToString(culture)}%",
                _ => amount(period.Cost, period.Tokens),
            },
            ["valueColor"] = warning is { } tone ? CardStyle.TextColor(tone) : "default",
            ["hasBar"] = percent is not null,
            ["bar"] = percent is { } filled
                ? UsageCharts.Progress(filled, warning is { } barTone ? CardStyle.ToneColor(barTone, light) : ClaudeOrange, light)
                : string.Empty,
            ["caption"] = DescribeReset(period, isWeek, now),
            ["shortCaption"] = shortCaption,
            ["spacing"] = first ? "default" : "large",
        };
    }

    // "Resets at 9:40 PM" or "Resets Thursday 11:00 AM", like Claude's usage page, plus when the percentage was reported
    // if that was a while ago.
    private static string DescribeReset(ClaudeUsagePeriod? period, bool isWeek, DateTimeOffset now)
    {
        var culture = CultureInfo.CurrentCulture;
        if (period is null)
        {
            return "Starts with your next message";
        }

        string reset = period.ResetsAt switch
        {
            null => "On this PC",
            { } at when isWeek => $"Resets {(period.ResetIsEstimate ? "about " : string.Empty)}{at.ToString("dddd", culture)} {at.ToString("t", culture)}",
            { } at => $"Resets at {(period.ResetIsEstimate ? "about " : string.Empty)}{at.ToString("t", culture)}",
        };

        if (period.Percent is null)
        {
            return $"{reset} · at API prices";
        }

        if (period.PercentAsOf is { } asOf && now - asOf > StalePercentAge)
        {
            string when = asOf.Date == now.Date
                ? asOf.ToString("t", culture)
                : $"{asOf.ToString("ddd", culture)} {asOf.ToString("t", culture)}";
            return $"{reset} · as of {when}";
        }

        return reset;
    }

    // The footer shows today's usage, or, until Claude reports percentages, a link to get them from Claude Code.
    private void DescribeFooter(JsonObject data, ClaudeUsageSummary usage, string todayAmount, DateTimeOffset now, bool light)
    {
        string? message;
        lock (_gate)
        {
            message = now - _setupMessageAt < SetupMessageDuration ? _setupMessage : null;
        }

        bool hasPercent = usage.Session?.Percent is not null || usage.Week.Percent is not null;
        data["footerMode"] = (hasPercent, message) switch
        {
            (false, { }) => "message",
            (false, null) when !ClaudeStatusLine.IsInstalled() => "setup",
            _ => "today",
        };
        data["footerMessage"] = message ?? string.Empty;

        var culture = CultureInfo.CurrentCulture;
        data["footer"] = usage.Today.Replies switch
        {
            0 => "Nothing from Claude Code today",
            1 => $"Today {todayAmount} · 1 reply",
            var replies => $"Today {todayAmount} · {replies.ToString(culture)} replies",
        };
        data["syncIcon"] = Octicons.Image(Octicon.Sync, CardStyle.ToneColor(LabelTone.Neutral, light));
    }

    // Adds Dev Pane as Claude Code's status line, which reports percent used after each reply. Runs only when the user
    // clicks the card's link, which says what it does.
    private void ShowLiveLimits()
    {
        string alias = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "devpane.exe");
        var result = ClaudeStatusLine.Install(alias);
        Log.Info($"Claude Code status line setup: {result}");

        string message = result switch
        {
            StatusLineSetupResult.Added or StatusLineSetupResult.AlreadyAdded =>
                "Added to Claude Code. Percent used shows after its next reply on a Pro or Max plan.",
            StatusLineSetupResult.OtherStatusLineInUse =>
                "Claude Code already has a status line, so Dev Pane left it as it is.",
            _ => "Couldn't update Claude Code's settings.",
        };

        lock (_gate)
        {
            _setupMessage = message;
            _setupMessageAt = DateTimeOffset.Now;
        }

        Push(includeTemplate: false);
    }
}
