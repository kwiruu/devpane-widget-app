using DevPane.Integrations.Claude;
using DevPane.Integrations.GitHub;
using DevPane.Integrations.Jira;
using DevPane.Integrations.LocalDev;
using DevPane.Integrations.SystemStats;
using DevPane.Integrations.Vercel;
using DevPane.Widgets.Cards;
using DevPane.Widgets.GitHub;
using DevPane.Widgets.Jira;
using DevPane.Widgets.Vercel;

namespace DevPane.Widgets.Previews;

/// <summary>
/// Made-up data for the widget picker's preview images: an "acme" organization with plausible numbers. Nothing here
/// comes from a real account. Times are relative to now, so text such as "Resets in 2h 14m" reads the same whenever
/// the previews are drawn.
/// </summary>
internal static class PreviewSamples
{
    private const double BytesPerMegabyte = 1024d * 1024;
    private const ulong Gigabyte = 1024UL * 1024 * 1024;

    private static DateTimeOffset Now => DateTimeOffset.Now;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public static GitHubSessionView GitHubAccount { get; } = new(GitHubSignInState.SignedIn, null, null, "sam-acme");

    public static VercelSessionView VercelAccount { get; } = new(VercelConnectionState.Connected, null);

    public static JiraSessionView JiraAccount { get; } = new(JiraConnectionState.Connected, null, "acme.atlassian.net");

    /// <summary>Only ever asked for links; it's never used to call Jira.</summary>
    public static JiraClient JiraClient { get; } =
        new(new JiraConnection("acme.atlassian.net", "sam@acme.example", string.Empty, "https://acme.atlassian.net"));

    public static SystemCard.Reading System => new(
        new SystemSnapshot
        {
            SystemDrive = @"C:\",
            CpuPercent = 23,
            MemoryUsedBytes = (ulong)(18.4 * Gigabyte),
            MemoryTotalBytes = (ulong)(31.7 * Gigabyte),
            GpuName = "GPU",
            GpuPercent = 8,
            GpuMemoryUsedBytes = (ulong)(2.1 * Gigabyte),
            GpuMemoryTotalBytes = 8 * Gigabyte,
            DiskFreeBytes = 412 * Gigabyte,
            DiskBytesPerSecond = 3.4 * BytesPerMegabyte,
            NetworkReceivedBytesPerSecond = 12.6 * 1_000_000 / 8,
            NetworkSentBytesPerSecond = 1.8 * 1_000_000 / 8,
        },
        Cpu: Wave(23, 9, 0.0),
        Ram: Wave(58, 2, 1.3),
        Gpu: Wave(8, 5, 2.1),
        Disk: Wave(3.4, 2.6, 0.7),
        Download: Wave(12.6, 7, 2.9),
        Upload: Wave(1.8, 1.2, 4.2));

    public static LocalDevSnapshot LocalDev => new(
        [
            new DevServer(3000, "node", IsExposed: false),
            new DevServer(5173, "node", IsExposed: false),
            new DevServer(8000, "python", IsExposed: true),
        ],
        [
            new DevProcessGroup("Code", 11, (ulong)(2.3 * Gigabyte)),
            new DevProcessGroup("node", 7, (ulong)(1.4 * Gigabyte)),
            new DevProcessGroup("python", 2, (ulong)(0.4 * Gigabyte)),
        ],
        VirtualMachineMemoryBytes: (ulong)(3.8 * Gigabyte));

    public static GitHubCard.Reading GitHub
    {
        get
        {
            var reviews = new List<PullRequestItem>
            {
                Pull(142, "Fix auth redirect loop on sign-out", "acme/web", "riley-acme", CheckState.Success, ReviewDecision.ReviewRequired, hours: 3),
                Pull(88, "Add rate-limit backoff to the sync worker", "acme/api", "jo-acme", CheckState.Pending, ReviewDecision.ReviewRequired, hours: 26),
            };

            var mine = new List<PullRequestItem>
            {
                Pull(151, "Cache project settings between requests", "acme/web", "sam-acme", CheckState.Success, ReviewDecision.Approved, hours: 5),
                Pull(93, "Move billing webhooks to the queue", "acme/api", "sam-acme", CheckState.Failure, ReviewDecision.ChangesRequested, hours: 30),
                Pull(37, "Document the deploy checklist", "acme/docs", "sam-acme", CheckState.Success, ReviewDecision.ReviewRequired, hours: 72),
            };

            var issues = new List<IssueItem>
            {
                new(204, "Search results flash before loading", "https://github.com/acme/web/issues/204", "acme/web", Now.AddHours(-8)),
                new(61, "Retry failed exports overnight", "https://github.com/acme/api/issues/61", "acme/api", Now.AddDays(-4)),
            };

            var overview = new GitHubOverview(
                "sam-acme",
                Avatar('S', "#6E56CF"),
                new SearchResult<PullRequestItem>(reviews.Count, reviews),
                new SearchResult<PullRequestItem>(mine.Count, mine),
                new SearchResult<IssueItem>(issues.Count, issues),
                WorkflowRuns: null);

            // A null repository is the card that covers every repository, which is what a newly pinned card shows.
            return new GitHubCard.Reading(null, overview, Now, null);
        }
    }

    public static ContributionsCard.Reading Contributions
    {
        get
        {
            // A fixed seed keeps the calendar identical from one render to the next.
            var random = new Random(20260301);
            var days = new List<ContributionDay>();
            int total = 0;
            for (int offset = 364; offset >= 0; offset--)
            {
                var date = Today.AddDays(-offset);
                bool weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

                // Quiet weekends, busy weekdays, and the last nine days unbroken so there's a streak to show.
                int count = offset < 9
                    ? random.Next(2, 9)
                    : random.NextDouble() < (weekend ? 0.25 : 0.78) ? random.Next(1, weekend ? 4 : 14) : 0;

                int level = count switch
                {
                    0 => 0,
                    < 3 => 1,
                    < 6 => 2,
                    < 10 => 3,
                    _ => 4,
                };

                days.Add(new ContributionDay(date, count, level));
                total += count;
            }

            return new ContributionsCard.Reading(new ContributionCalendar("sam-acme", total, days), Now, null);
        }
    }

    public static ClaudeUsageSummary Claude
    {
        get
        {
            decimal[] costs = [21.10m, 34.80m, 12.40m, 0m, 28.90m, 41.30m, 18.40m];
            var week = costs
                .Select((cost, index) => new ClaudeDailyUsage(Today.AddDays(index - (costs.Length - 1)), cost, (long)(cost * 52_000)))
                .ToList();

            // Percentages present means the plan limits are known, which is the card with nothing left to set up.
            return new ClaudeUsageSummary(
                LogsFound: true,
                Today: new ClaudeUsageTotals(18.40m, 956_800, 117, 3),
                LastSevenDays: week,
                Session: new ClaudeUsagePeriod(Now.AddHours(-2).AddMinutes(-46), Now.AddHours(2).AddMinutes(14), false, 18.40m, 956_800, 42, Now),
                Week: new ClaudeUsagePeriod(Now.AddDays(-4), NextThursdayAt11(), false, 156.90m, 8_158_800, 61, Now),
                ProjectsToday:
                [
                    new ClaudeProjectUsage("web", 9.80m, 509_600),
                    new ClaudeProjectUsage("api", 6.10m, 317_200),
                    new ClaudeProjectUsage("docs", 2.50m, 130_000),
                ]);
        }
    }

    public static VercelCard.Reading Vercel
    {
        get
        {
            var web = Deployment("web", VercelDeploymentState.Ready, "main", "Cache project settings between requests", minutesAgo: 12);
            var docs = Deployment("docs", VercelDeploymentState.Building, "main", "Document the deploy checklist", minutesAgo: 2);
            var api = Deployment("api", VercelDeploymentState.Error, "main", "Move billing webhooks to the queue", minutesAgo: 47);

            var projects = new List<VercelCard.ProjectSummary>
            {
                Project("docs", "docs.acme.dev", docs, live: Deployment("docs", VercelDeploymentState.Ready, "main", "Fix broken anchors", minutesAgo: 1440)),
                Project("web", "acme.dev", web, live: web),
                Project("api", "api.acme.dev", api, live: Deployment("api", VercelDeploymentState.Ready, "main", "Add request IDs to logs", minutesAgo: 2880)),
            };

            return new VercelCard.Reading(VercelCard.Scope.Everything, projects, null, Now, null);
        }
    }

    public static JiraCard.Reading Jira => new(
        JiraCard.Filter.Default,
        [
            Issue("WEB-412", "Checkout button unresponsive on Safari", "In Progress", JiraStatusCategory.InProgress, "Bug", "Highest", dueInDays: -1),
            Issue("WEB-398", "Add audit log export", "In Progress", JiraStatusCategory.InProgress, "Story", "High", dueInDays: 3),
            Issue("WEB-405", "Update onboarding copy", "To Do", JiraStatusCategory.ToDo, "Task", "Medium", dueInDays: 6),
            Issue("WEB-377", "Archive unused feature flags", "To Do", JiraStatusCategory.ToDo, "Task", "Low", dueInDays: null),
            Issue("WEB-366", "Migrate settings page to new forms", "To Do", JiraStatusCategory.ToDo, "Story", "Medium", dueInDays: 12),
        ],
        HasMore: false,
        UpdatedAt: Now,
        Problem: null);

    // A gently moving history that ends exactly on the current value, so the graph and the number agree.
    private static double[] Wave(double current, double swing, double phase)
    {
        const int length = 30;
        var values = new double[length];
        for (int i = 0; i < length; i++)
        {
            double t = (double)i / (length - 1);
            values[i] = Math.Max(0, current + swing * Math.Sin(t * 7.0 + phase) * (1 - t) + swing * 0.35 * Math.Sin(t * 19.0 + phase));
        }

        values[^1] = current;
        return values;
    }

    private static PullRequestItem Pull(
        int number, string title, string repository, string author, CheckState checks, ReviewDecision review, int hours) =>
        new(number, title, $"https://github.com/{repository}/pull/{number}", repository, author, false, checks, review, Now.AddHours(-hours));

    private static VercelDeployment Deployment(string project, VercelDeploymentState state, string branch, string message, int minutesAgo)
    {
        var created = Now.AddMinutes(-minutesAgo);
        bool finished = state is VercelDeploymentState.Ready or VercelDeploymentState.Error;
        return new VercelDeployment(
            $"dpl_{project}{minutesAgo}",
            $"prj_{project}",
            project,
            state,
            IsProduction: true,
            Url: $"{project}-git-{branch}-acme.vercel.app",
            InspectorUrl: $"https://vercel.com/acme/{project}",
            Branch: branch,
            CommitMessage: message,
            CreatedAt: created,
            Creator: "sam-acme",
            BuildingAt: created.AddSeconds(4),
            ReadyAt: finished ? created.AddSeconds(state == VercelDeploymentState.Ready ? 52 : 31) : null,
            ErrorMessage: state == VercelDeploymentState.Error ? "Command \"npm run build\" exited with 1" : null);
    }

    private static VercelCard.ProjectSummary Project(string name, string domain, VercelDeployment production, VercelDeployment live) =>
        new(new VercelProject($"prj_{name}", name, domain, $"acme/{name}", "github", live), production, $"https://vercel.com/acme/{name}", Icon: null);

    private static JiraIssue Issue(
        string key, string summary, string status, JiraStatusCategory category, string type, string priority, int? dueInDays) =>
        new(key, summary, status, category, type, 0, priority, dueInDays is int days ? Today.AddDays(days) : null, Now.AddHours(-6), "WEB", "Acme Web");

    // A plain initial on a colored circle: what a profile picture looks like without being anyone's.
    private static string Avatar(char initial, string color) => CardStyle.DataUri(
        "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32' viewBox='0 0 32 32'>"
        + $"<circle cx='16' cy='16' r='16' fill='{color}'/>"
        + $"<text x='16' y='21.5' font-family='Segoe UI, sans-serif' font-size='15' font-weight='600' fill='#FFFFFF' text-anchor='middle'>{initial}</text>"
        + "</svg>");

    private static DateTimeOffset NextThursdayAt11()
    {
        int days = ((int)DayOfWeek.Thursday - (int)Today.DayOfWeek + 7) % 7;
        var date = Today.AddDays(days == 0 ? 7 : days);
        return new DateTimeOffset(date.ToDateTime(new TimeOnly(11, 0)), Now.Offset);
    }
}
