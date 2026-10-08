using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevPane.Integrations.Native;
using DevPane.Integrations.Vercel;
using DevPane.Integrations.Web;
using DevPane.Widgets.Vercel;
using Microsoft.Windows.Widgets;

namespace DevPane.Widgets.Cards;

/// <summary>
/// Vercel projects as cards, like Vercel's dashboard: each project's icon, name, domain, production status, latest
/// production commit, and repository. Customize picks everything the token can see, one team, or one project; a card
/// for one project also shows its latest deployment in detail, like its page on Vercel: domains, Git source, the end of
/// its build log in a terminal, and buttons to share, read the logs, and visit it.
/// </summary>
internal sealed class VercelCard : PollingCard<VercelCard.Reading>
{
    public const string DefinitionId = "Vercel";

    private const string CustomizeTemplate = "VercelCustomize";
    private const string EverythingChoice = "all";
    private const string TokensUrl = "https://vercel.com/account/tokens";

    // How many project cards fit, and how tall each is. Every size keeps the same gap under the header and the same
    // padding inside the cards, so smaller cards leave out lines instead: medium the repository, small also the commit.
    // Content taller than the card makes the Widgets Board draw nothing.
    private const int MediumCards = 2;
    private const int LargeCards = 3;
    private const int SmallCardHeight = 62;
    private const int MediumCardHeight = 88;
    private const int LargeCardHeight = 106;

    // Build log lines shown for one project, and on large, the domains listed above them. Large also fits the Git source
    // and a row of buttons, so it shows fewer log lines than it could alone.
    private const int MediumLogLines = 2;
    private const int LargeLogLines = 3;
    private const int LargeDomains = 2;

    // Opening the board again within this window reuses the last result instead of calling Vercel.
    private static readonly TimeSpan MinimumRefreshAge = TimeSpan.FromSeconds(30);

    // How long the Share button says "Copied".
    private static readonly TimeSpan CopiedDuration = TimeSpan.FromSeconds(3);

    private readonly object _gate = new();
    private Scope _scope;
    private volatile bool _refreshRequested;
    private bool _customizing;
    private IReadOnlyList<ScopeChoice>? _choices;
    private DateTimeOffset _copiedUntil;

    // A finished deployment's build log doesn't change, so it's fetched once.
    private (string DeploymentId, IReadOnlyList<VercelLogLine> Lines)? _finishedLog;

    public VercelCard(string id, WidgetSize size, string? customState)
        : base(id, size, customState)
    {
        _scope = Scope.Read(customState);
        VercelSession.Changed += OnSessionChanged;
    }

    /// <param name="TeamId">The team to show, or null for the token's default.</param>
    /// <param name="ProjectId">The project to show, or null for all projects.</param>
    /// <param name="Label">The scope's name for the card's footer, such as "Acme / web".</param>
    internal sealed record Scope(string? TeamId, string? ProjectId, string Label)
    {
        public static Scope Everything { get; } = new(null, null, "Vercel");

        // Custom state is saved by Windows with the card: {"teamId":"…","projectId":"…","label":"…"}, or empty.
        public static Scope Read(string? customState)
        {
            if (string.IsNullOrWhiteSpace(customState))
            {
                return Everything;
            }

            try
            {
                return JsonNode.Parse(customState) is JsonObject state
                    ? new Scope(
                        state["teamId"]?.GetValue<string>(),
                        state["projectId"]?.GetValue<string>(),
                        state["label"]?.GetValue<string>() ?? Everything.Label)
                    : Everything;
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException)
            {
                return Everything;
            }
        }

        public string Write() => this == Everything
            ? string.Empty
            : new JsonObject { ["teamId"] = TeamId, ["projectId"] = ProjectId, ["label"] = Label }.ToJsonString();
    }

    /// <param name="Projects">The most recently deployed projects first.</param>
    /// <param name="Problem">Why the last refresh failed, if it did.</param>
    /// <param name="Detail">For a card showing one project: its deployments and build log. Null for several projects.</param>
    internal sealed record Reading(
        Scope Scope,
        IReadOnlyList<ProjectSummary> Projects,
        ProjectDetail? Detail,
        DateTimeOffset? UpdatedAt,
        string? Problem);

    /// <param name="Deployments">The project's newest deployments, production and preview, newest first.</param>
    /// <param name="BuildLog">The last lines of the newest deployment's build log, oldest first.</param>
    /// <param name="Domains">The newest deployment's domains, in the order <see cref="OrderDomains"/> gives them.</param>
    internal sealed record ProjectDetail(
        IReadOnlyList<VercelDeployment> Deployments,
        IReadOnlyList<VercelLogLine> BuildLog,
        IReadOnlyList<string> Domains);

    // What a deployment's domain is, for its icon: a custom domain, the branch's vercel.app domain, the deployment's
    // own URL, or another vercel.app domain.
    private enum DomainKind
    {
        Custom,
        Branch,
        Deployment,
        VercelApp,
    }

    /// <param name="Production">The newest production deployment, which may still be building or may have failed.</param>
    /// <param name="DashboardUrl">The project's page on Vercel.</param>
    /// <param name="Icon">The site's icon, or null to show the project's initial instead.</param>
    internal sealed record ProjectSummary(VercelProject Project, VercelDeployment? Production, string DashboardUrl, string? Icon);

    private sealed record ScopeChoice(string Value, string Title, Scope Scope);

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

    // Deployments change quickly while building, and each refresh is two small API requests.
    protected override TimeSpan Interval => TimeSpan.FromSeconds(60);

    // Updating a card redraws it and clears what's typed in its inputs, so the connect screen isn't updated on a timer.
    protected override bool PushAfterSample => VercelSession.View.State == VercelConnectionState.Connected;

    public override void Dispose()
    {
        VercelSession.Changed -= OnSessionChanged;
        base.Dispose();
    }

    public override void OnAction(string verb, string data)
    {
        switch (verb)
        {
            case "connect":
                _ = VercelSession.ConnectAsync(CardInputs.Read(data).GetValueOrDefault("token") ?? string.Empty);
                break;
            case "refresh":
                _refreshRequested = true;
                RefreshNow();
                break;
            case "share":
                ShareNewestDeployment();
                break;
            case "saveCustomization":
                SaveCustomization(CardInputs.Read(data).GetValueOrDefault("scope"));
                break;
            case "cancelCustomization":
                ExitCustomization();
                break;
            case "disconnect":
                VercelSession.Disconnect();
                ExitCustomization();
                break;
        }
    }

    public override void OnCustomizationRequested()
    {
        lock (_gate)
        {
            _customizing = true;
            _choices = null;
        }

        Push(includeTemplate: true);
        _ = LoadChoicesAsync();
    }

    protected override async ValueTask<Reading> TakeSampleAsync()
    {
        Scope scope;
        lock (_gate)
        {
            scope = _scope;
        }

        var latest = Latest is { } previous && previous.Scope == scope ? previous : null;
        if (VercelSession.Client is not { } client)
        {
            return new Reading(scope, [], null, null, null);
        }

        bool requested = _refreshRequested;
        _refreshRequested = false;
        if (!requested && latest?.UpdatedAt is { } updatedAt && DateTimeOffset.Now - updatedAt < MinimumRefreshAge)
        {
            return latest;
        }

        try
        {
            return scope.ProjectId is { } projectId
                ? await LoadProjectDetailAsync(client, scope, projectId)
                : new Reading(scope, await LoadProjectsAsync(client, scope), null, DateTimeOffset.Now, null);
        }
        catch (VercelUnauthorizedException)
        {
            VercelSession.HandleUnauthorized(client);
            return new Reading(scope, [], null, null, null);
        }
        catch (Exception e) when (DescribeFailure(e, scope, latest is not null) is { } problem)
        {
            Log.Error("Vercel refresh failed", e);
            return (latest ?? new Reading(scope, [], null, null, null)) with { Problem = problem };
        }
    }

    private async Task<Reading> LoadProjectDetailAsync(VercelClient client, Scope scope, string projectId)
    {
        var projectRequest = client.GetProjectAsync(scope.TeamId, projectId, CancellationToken.None);
        var deploymentsRequest = client.GetDeploymentsAsync(scope.TeamId, projectId, productionOnly: false, limit: 8, CancellationToken.None);
        await Task.WhenAll(projectRequest, deploymentsRequest);

        var project = projectRequest.Result;
        var deployments = deploymentsRequest.Result;
        var newestProduction = deployments.FirstOrDefault(deployment => deployment.IsProduction);
        var production = newestProduction is not null && (project.Live is null || newestProduction.CreatedAt >= project.Live.CreatedAt)
            ? newestProduction
            : project.Live;
        string? icon = project.Domain is { } domain ? await SiteIcons.FindAsync(domain, CancellationToken.None) : null;
        var summary = new ProjectSummary(project, production, DashboardUrl(deployments.FirstOrDefault(), project), icon);

        if (deployments.Count == 0)
        {
            return new Reading(scope, [summary], new ProjectDetail(deployments, [], []), DateTimeOffset.Now, null);
        }

        var buildLog = LoadBuildLogAsync(client, scope, deployments[0]);
        var aliases = LoadAliasesAsync(client, scope, deployments[0]);
        await Task.WhenAll(buildLog, aliases);

        var detail = new ProjectDetail(deployments, buildLog.Result, OrderDomains(deployments[0], aliases.Result));
        return new Reading(scope, [summary], detail, DateTimeOffset.Now, null);
    }

    private async Task<IReadOnlyList<VercelLogLine>> LoadBuildLogAsync(VercelClient client, Scope scope, VercelDeployment deployment)
    {
        if (_finishedLog is { } cached && cached.DeploymentId == deployment.Id)
        {
            return cached.Lines;
        }

        try
        {
            var lines = await client.GetBuildLogAsync(scope.TeamId, deployment.Id, LargeLogLines, CancellationToken.None);
            if (deployment.State is VercelDeploymentState.Ready or VercelDeploymentState.Error or VercelDeploymentState.Canceled)
            {
                _finishedLog = (deployment.Id, lines);
            }

            return lines;
        }
        catch (Exception e) when (IsOptionalFailure(e))
        {
            // The rest of the card is still worth showing without the log.
            Log.Info($"Loading a Vercel build log failed: {e.Message}");
            return [];
        }
    }

    private static async Task<IReadOnlyList<string>> LoadAliasesAsync(VercelClient client, Scope scope, VercelDeployment deployment)
    {
        try
        {
            return await client.GetDeploymentAliasesAsync(scope.TeamId, deployment.Id, CancellationToken.None);
        }
        catch (Exception e) when (IsOptionalFailure(e))
        {
            // The card still lists the deployment's own URL.
            Log.Info($"Loading a Vercel deployment's domains failed: {e.Message}");
            return [];
        }
    }

    // Failures that leave out one part of a project's detail. A rejected token or the rate limit fails the whole refresh.
    private static bool IsOptionalFailure(Exception exception) =>
        exception is (VercelException and not VercelUnauthorizedException and not VercelRateLimitException)
            or HttpRequestException or TaskCanceledException or JsonException;

    // Like the deployment's page on Vercel: custom domains first, then the branch's vercel.app domain, then the
    // deployment's own URL, then the rest, such as www domains.
    private static IReadOnlyList<string> OrderDomains(VercelDeployment deployment, IReadOnlyList<string> aliases) =>
        (deployment.Url is { } url ? aliases.Append(url) : aliases)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(domain => KindOf(domain, deployment) switch
            {
                DomainKind.Custom when !domain.StartsWith("www.", StringComparison.OrdinalIgnoreCase) => 0,
                DomainKind.Branch => 1,
                DomainKind.Deployment => 2,
                DomainKind.Custom => 3,
                _ => 4,
            })
            .ThenBy(domain => domain.Length)
            .ToList();

    private static DomainKind KindOf(string domain, VercelDeployment deployment) => domain switch
    {
        _ when string.Equals(domain, deployment.Url, StringComparison.OrdinalIgnoreCase) => DomainKind.Deployment,
        _ when !domain.EndsWith(".vercel.app", StringComparison.OrdinalIgnoreCase) => DomainKind.Custom,
        // Vercel names a branch's domain "<project>-git-<branch>-<scope>.vercel.app".
        _ when domain.Contains("-git-", StringComparison.OrdinalIgnoreCase) => DomainKind.Branch,
        _ => DomainKind.VercelApp,
    };

    private static async Task<IReadOnlyList<ProjectSummary>> LoadProjectsAsync(VercelClient client, Scope scope)
    {
        // Projects give domains and repositories; production deployments add builds in progress and dashboard links.
        var projects = client.GetProjectsAsync(scope.TeamId, CancellationToken.None);
        var deployments = client.GetDeploymentsAsync(scope.TeamId, projectId: null, productionOnly: true, limit: 20, CancellationToken.None);
        await Task.WhenAll(projects, deployments);

        var newestByProject = deployments.Result
            .GroupBy(deployment => deployment.ProjectId)
            .ToDictionary(group => group.Key, group => group.First());

        var summaries = projects.Result
            .Select(project =>
            {
                newestByProject.TryGetValue(project.Id, out var newest);
                var production = newest is not null && (project.Live is null || newest.CreatedAt >= project.Live.CreatedAt)
                    ? newest
                    : project.Live;
                return (Project: project, Production: production, DashboardUrl: DashboardUrl(newest, project));
            })
            .OrderByDescending(summary => summary.Production?.CreatedAt ?? DateTimeOffset.MinValue)
            .Take(LargeCards)
            .ToList();

        var icons = await Task.WhenAll(summaries.Select(summary => summary.Project.Domain is { } domain
            ? SiteIcons.FindAsync(domain, CancellationToken.None)
            : Task.FromResult<string?>(null)));

        return summaries
            .Select((summary, index) => new ProjectSummary(summary.Project, summary.Production, summary.DashboardUrl, icons[index]))
            .ToList();
    }

    // A deployment's inspector link is the project's page plus the deployment ID, so dropping the last part gives the
    // project's page.
    private static string DashboardUrl(VercelDeployment? deployment, VercelProject project)
    {
        const int schemeLength = 8; // "https://"
        if (deployment?.InspectorUrl is { } inspector && inspector.LastIndexOf('/') is var slash and > schemeLength)
        {
            return inspector[..slash];
        }

        return project.Domain is { } domain ? $"https://{domain}" : "https://vercel.com/dashboard";
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

        var session = VercelSession.View;
        bool light = WindowsTheme.IsLight();
        var data = new JsonObject
        {
            ["state"] = session.State switch
            {
                VercelConnectionState.Connected => "connected",
                VercelConnectionState.Checking => "checking",
                _ => "disconnected",
            },
            ["notice"] = session.Notice ?? string.Empty,
            ["tokensUrl"] = TokensUrl,
            ["mark"] = VercelStyle.Mark(light),
            ["markSize"] = Size == WidgetSize.Small ? "28px" : "40px",
            // The connect screen is centered in this much height. Centering in more height than the card has makes the
            // Widgets Board draw nothing, so these match the GitHub cards' sign-in screens.
            ["centerHeight"] = Size switch
            {
                WidgetSize.Small => "0px",
                WidgetSize.Medium => "220px",
                _ => "360px",
            },
        };

        var (buttonLeft, buttonFill, buttonRight) = VercelStyle.Button(light);
        data["buttonLeft"] = buttonLeft;
        data["buttonFill"] = buttonFill;
        data["buttonRight"] = buttonRight;
        data["buttonText"] = VercelStyle.ButtonTextColor(light);

        if (session.State != VercelConnectionState.Connected)
        {
            return data;
        }

        Scope scope;
        lock (_gate)
        {
            scope = _scope;
        }

        var current = reading?.Scope == scope ? reading : null;

        // One project on medium and large: a compact project card, then the details below it.
        bool detail = scope.ProjectId is not null && Size != WidgetSize.Small;
        data["mode"] = detail ? "project" : "projects";
        var (count, height) = Size switch
        {
            _ when detail => (1, SmallCardHeight),
            WidgetSize.Small => (1, SmallCardHeight),
            WidgetSize.Medium => (MediumCards, MediumCardHeight),
            _ => (LargeCards, LargeCardHeight),
        };

        var newest = detail ? current?.Detail?.Deployments.FirstOrDefault() : null;
        if (detail && current?.Detail is { } projectDetail)
        {
            DescribeProjectDetail(data, projectDetail, current.Projects.FirstOrDefault()?.Project, light);
        }

        var projects = (current?.Projects ?? []).Take(count).ToList();
        data["hasProjects"] = projects.Count > 0;
        data["projects"] = new JsonArray(projects
            .Select((summary, index) => (JsonNode)ProjectCard(summary, height, first: index == 0, light, newest))
            .ToArray());
        data["empty"] = current?.UpdatedAt is null ? "Loading from Vercel…" : "No projects yet.";

        string freshness = (current?.UpdatedAt, current?.Problem) switch
        {
            (_, { } problem) => problem,
            ({ } updatedAt, _) => $"Updated {Format.RelativeTime(updatedAt)}",
            _ => "Loading from Vercel…",
        };
        data["footer"] = $"{scope.Label} · {freshness}";
        data["syncIcon"] = Octicons.Image(Octicon.Sync, VercelStyle.MutedColor(light));
        return data;
    }

    // The newest deployment, like its page on Vercel. Large lists its domains and Git source, then the end of its build
    // log in a terminal and buttons to share, read the logs, and visit it; medium has its commit and a shorter log.
    private void DescribeProjectDetail(JsonObject data, ProjectDetail detail, VercelProject? project, bool light)
    {
        bool large = Size == WidgetSize.Large;
        data["hasDeployment"] = detail.Deployments.Count > 0;
        if (detail.Deployments.Count == 0)
        {
            data["noDeployment"] = "No deployments yet.";
            return;
        }

        var newest = detail.Deployments[0];
        string muted = VercelStyle.MutedColor(light);
        string deploymentUrl = LinkFor(newest);
        data["deployUrl"] = deploymentUrl;
        data["commitIcon"] = Octicons.Image(Octicon.GitCommit, muted);
        data["deployCommit"] = newest.CommitMessage ?? newest.Url ?? newest.ProjectName;

        var domains = large ? detail.Domains.Take(LargeDomains).ToList() : [];
        data["hasDomains"] = domains.Count > 0;
        data["domains"] = new JsonArray(domains
            .Select(domain => (JsonNode)new JsonObject
            {
                ["name"] = domain,
                ["url"] = $"https://{domain}",
                ["icon"] = Octicons.Image(KindOf(domain, newest) switch
                {
                    DomainKind.Branch => Octicon.GitBranch,
                    DomainKind.Deployment => Octicon.GitCommit,
                    _ => Octicon.Globe,
                }, muted),
            })
            .ToArray());

        data["hasSource"] = large && (newest.Branch is not null || newest.CommitSha is not null);
        data["branch"] = newest.Branch ?? string.Empty;
        data["branchUrl"] = newest.Branch is { } branch ? BranchUrl(project, branch) ?? deploymentUrl : deploymentUrl;
        data["branchIcon"] = Octicons.Image(Octicon.GitBranch, muted);
        data["commitSha"] = newest.CommitSha is { } sha ? sha[..Math.Min(7, sha.Length)] : string.Empty;
        data["commitUrl"] = newest.CommitSha is { } fullSha ? CommitUrl(project, fullSha) ?? deploymentUrl : deploymentUrl;

        int logLines = large ? LargeLogLines : MediumLogLines;
        var log = detail.BuildLog.TakeLast(logLines).ToList();
        string terminalText = VercelStyle.TerminalTextColor(light);
        data["terminalText"] = terminalText;
        data["terminalIcon"] = Octicons.Image(Octicon.Terminal, VercelStyle.TerminalIconColor);
        data["terminalStatus"] = TerminalStatus(newest);
        data["terminalStatusColor"] = newest.State == VercelDeploymentState.Error ? "attention" : terminalText;
        data["hasLog"] = log.Count > 0;
        data["log"] = new JsonArray(log
            .Select((line, index) => (JsonNode)new JsonObject
            {
                ["text"] = line.Text,
                ["color"] = line.IsError ? "attention" : terminalText,
                // The last line is the brightest, like a terminal's latest output.
                ["subtle"] = index < log.Count - 1,
            })
            .ToArray());

        // A failed deployment whose log couldn't be loaded still says why it failed.
        data["noLog"] = newest.State == VercelDeploymentState.Error && newest.ErrorMessage is { } error ? error : "No build log yet.";

        // Each log line takes about 18px, and the title bar and padding 38px more; the terminal is drawn to fit.
        int terminalHeight = logLines * 18 + 38;
        var terminal = VercelStyle.Terminal(light, terminalHeight);
        data["logHeight"] = $"{terminalHeight}px";
        data["logLeft"] = terminal.Left;
        data["logFill"] = terminal.Fill;
        data["logRight"] = terminal.Right;

        string? visitDomain = detail.Domains.FirstOrDefault();
        data["showButtons"] = large;
        data["visitUrl"] = visitDomain is null ? deploymentUrl : $"https://{visitDomain}";
        data["visitName"] = visitDomain ?? "this deployment";
        lock (_gate)
        {
            data["shareLabel"] = DateTimeOffset.Now < _copiedUntil ? "Copied" : "Share";
        }

        data["shareIcon"] = Octicons.Image(Octicon.Share, VercelStyle.SmallButtonIconColor(light, primary: false));
        data["logsIcon"] = Octicons.Image(Octicon.ListUnordered, VercelStyle.SmallButtonIconColor(light, primary: false));
        data["visitIcon"] = Octicons.Image(Octicon.Globe, VercelStyle.SmallButtonIconColor(light, primary: true));

        var secondary = VercelStyle.SmallButton(light, primary: false);
        data["secondaryLeft"] = secondary.Left;
        data["secondaryFill"] = secondary.Fill;
        data["secondaryRight"] = secondary.Right;
        var primary = VercelStyle.SmallButton(light, primary: true);
        data["primaryLeft"] = primary.Left;
        data["primaryFill"] = primary.Fill;
        data["primaryRight"] = primary.Right;
    }

    // What the right of the terminal's title bar says: how long the build took or has been running, or how it ended.
    private static string TerminalStatus(VercelDeployment deployment) => deployment switch
    {
        { State: VercelDeploymentState.Error } => "failed",
        { State: VercelDeploymentState.Canceled } => "canceled",
        { BuildDuration: { } duration } => FormatDuration(duration),
        { State: VercelDeploymentState.Building or VercelDeploymentState.Initializing, BuildingAt: { } started } =>
            FormatDuration(DateTimeOffset.Now - started),
        { State: VercelDeploymentState.Queued } => "queued",
        _ => string.Empty,
    };

    private static string? BranchUrl(VercelProject? project, string branch) => (project?.GitProvider, project?.Repository) switch
    {
        ("github", { } repository) => $"https://github.com/{repository}/tree/{EscapeBranch(branch)}",
        ("gitlab", { } repository) => $"https://gitlab.com/{repository}/-/tree/{EscapeBranch(branch)}",
        ("bitbucket", { } repository) => $"https://bitbucket.org/{repository}/src/{EscapeBranch(branch)}",
        _ => null,
    };

    private static string? CommitUrl(VercelProject? project, string sha) => (project?.GitProvider, project?.Repository) switch
    {
        ("github", { } repository) => $"https://github.com/{repository}/commit/{sha}",
        ("gitlab", { } repository) => $"https://gitlab.com/{repository}/-/commit/{sha}",
        ("bitbucket", { } repository) => $"https://bitbucket.org/{repository}/commits/{sha}",
        _ => null,
    };

    // Branch names can contain slashes, which Git hosts expect unescaped in the path.
    private static string EscapeBranch(string branch) => string.Join('/', branch.Split('/').Select(Uri.EscapeDataString));

    // A widget can't open Vercel's share dialog, so Share copies the link people would visit, and the button says
    // "Copied" for a moment.
    private void ShareNewestDeployment()
    {
        if (Latest?.Detail?.Domains.FirstOrDefault() is not { } domain)
        {
            return;
        }

        if (!ClipboardText.TrySet($"https://{domain}"))
        {
            Log.Info("Copying a Vercel deployment's link failed: the clipboard is busy");
            return;
        }

        lock (_gate)
        {
            _copiedUntil = DateTimeOffset.Now + CopiedDuration;
        }

        Push(includeTemplate: false);
        _ = RestoreShareLabelAsync();
    }

    private async Task RestoreShareLabelAsync()
    {
        await Task.Delay(CopiedDuration);
        try
        {
            if (IsActive)
            {
                Push(includeTemplate: false);
            }
        }
        catch (Exception e)
        {
            Log.Error("Vercel card update after copying a link failed", e);
        }
    }

    // "42s" or "1m 5s".
    private static string FormatDuration(TimeSpan duration) => duration.TotalMinutes < 1
        ? $"{Math.Max(1, (int)duration.TotalSeconds)}s"
        : $"{(int)duration.TotalMinutes}m {duration.Seconds}s";

    private static string LinkFor(VercelDeployment deployment) =>
        deployment.InspectorUrl ?? (deployment.Url is { } url ? $"https://{url}" : "https://vercel.com/dashboard");

    /// <param name="newest">
    /// On a card for one project, its newest deployment, which the project card then describes in place of the domain.
    /// </param>
    private static JsonObject ProjectCard(ProjectSummary summary, int height, bool first, bool light, VercelDeployment? newest)
    {
        var project = summary.Project;
        var production = summary.Production;
        string muted = VercelStyle.MutedColor(light);
        var state = newest?.State ?? production?.State;

        string commit = (production, production?.CommitMessage) switch
        {
            (null, _) => "No production deployment yet",
            (_, { } message) => message,
            _ => "Deployed without a commit",
        };

        // "kwiruu/web-portfolio · Sep 4", like Vercel's project cards. Without the repository line, the date sits beside
        // the commit instead.
        string date = production?.CreatedAt.ToLocalTime().ToString("MMM d", CultureInfo.CurrentCulture) ?? string.Empty;
        string repository = project.Repository ?? "Not connected to Git";
        bool showRepository = height >= LargeCardHeight;

        var surface = VercelStyle.ProjectCard(light, height);
        return new JsonObject
        {
            ["name"] = project.Name,
            ["domain"] = newest is null
                ? project.Domain ?? "No domain yet"
                : $"{StatusText(newest.State)} · {(newest.IsProduction ? "Production" : "Preview")} · {Format.Age(newest.CreatedAt)}",
            ["url"] = summary.DashboardUrl,
            ["hasIcon"] = summary.Icon is not null,
            ["icon"] = summary.Icon ?? string.Empty,
            ["initial"] = project.Name.Length > 0 ? char.ToUpperInvariant(project.Name[0]).ToString() : "?",
            ["tile"] = VercelStyle.InitialTile(light),
            ["ring"] = VercelStyle.StatusRing(state, light),
            ["status"] = StatusText(state),
            ["showCommit"] = height > SmallCardHeight,
            ["commit"] = commit,
            ["commitIcon"] = Octicons.Image(Octicon.GitCommit, muted),
            ["showRepo"] = showRepository,
            ["repo"] = date.Length > 0 ? $"{repository} · {date}" : repository,
            ["date"] = showRepository ? string.Empty : date,
            ["repoIcon"] = project.GitProvider == "github" ? GitHubStyle.MarkIn(muted) : Octicons.Image(Octicon.GitBranch, muted),
            ["cardHeight"] = $"{height}px",
            ["cardLeft"] = surface.Left,
            ["cardFill"] = surface.Fill,
            ["cardRight"] = surface.Right,
            ["spacing"] = first ? "none" : "default",
        };
    }

    // The words Vercel's dashboard uses for a deployment's state.
    private static string StatusText(VercelDeploymentState? state) => state switch
    {
        VercelDeploymentState.Ready => "Ready",
        VercelDeploymentState.Error => "Error",
        VercelDeploymentState.Building or VercelDeploymentState.Initializing => "Building",
        VercelDeploymentState.Queued => "Queued",
        VercelDeploymentState.Blocked => "Blocked",
        VercelDeploymentState.Canceled => "Canceled",
        VercelDeploymentState.Deleted => "Deleted",
        null => "Not deployed",
        _ => "Unknown",
    };

    private static string? DescribeFailure(Exception exception, Scope scope, bool hasEarlierData) => exception switch
    {
        VercelRateLimitException rateLimit =>
            $"Vercel's rate limit was reached. It resets at {rateLimit.ResetAt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)}.",
        VercelForbiddenException => $"This token can't see {scope.Label}. Pick another in Customize.",
        VercelException or HttpRequestException or TaskCanceledException => hasEarlierData
            ? "Couldn't reach Vercel. Showing the last update."
            : "Couldn't reach Vercel. Trying again in a minute.",
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
            if (VercelSession.View.State == VercelConnectionState.Connected)
            {
                _refreshRequested = true;
                RefreshNow();
            }
        }
        catch (Exception e)
        {
            Log.Error("Vercel card update after a connection change failed", e);
        }
    }

    // Customization: everything the token can see, one team, or one project.

    private JsonObject DescribeCustomization()
    {
        string selected = EverythingChoice;
        var choices = new JsonArray();
        if (_choices is { } loaded)
        {
            foreach (var choice in loaded)
            {
                choices.Add(new JsonObject { ["value"] = choice.Value, ["title"] = choice.Title });
                if (choice.Scope.TeamId == _scope.TeamId && choice.Scope.ProjectId == _scope.ProjectId)
                {
                    selected = choice.Value;
                }
            }
        }

        return new JsonObject
        {
            ["loading"] = _choices is null,
            ["selected"] = selected,
            ["choices"] = choices,
            ["connected"] = VercelSession.View.State == VercelConnectionState.Connected,
        };
    }

    private async Task LoadChoicesAsync()
    {
        var choices = new List<ScopeChoice>();
        if (VercelSession.Client is { } client)
        {
            // Personal projects, then each team's. Only full-account tokens reach the personal account; team- and
            // project-scoped tokens list just their own team, and anything they can't list is skipped.
            var scopes = new List<(string? TeamId, string Name)>();
            try
            {
                if (await client.IsFullAccountTokenAsync(CancellationToken.None))
                {
                    scopes.Add((null, "Personal account"));
                }

                scopes.AddRange((await client.GetTeamsAsync(CancellationToken.None)).Take(10).Select(team => ((string?)team.Id, team.Name)));
            }
            catch (Exception e) when (e is VercelException or HttpRequestException or TaskCanceledException)
            {
                Log.Info($"Listing Vercel teams failed: {e.Message}");
            }

            foreach (var (teamId, name) in scopes)
            {
                try
                {
                    var projects = await client.GetProjectsAsync(teamId, CancellationToken.None);
                    string teamValue = teamId ?? "personal";
                    choices.Add(new ScopeChoice($"team:{teamValue}", $"{name}: all projects", new Scope(teamId, null, name)));
                    choices.AddRange(projects
                        .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(project => new ScopeChoice(
                            $"project:{teamValue}:{project.Id}", $"{name} / {project.Name}", new Scope(teamId, project.Id, $"{name} / {project.Name}"))));
                }
                catch (Exception e) when (e is VercelException or HttpRequestException or TaskCanceledException)
                {
                    Log.Info($"Listing Vercel projects for {name} failed: {e.Message}");
                }
            }
        }

        bool stillCustomizing;
        lock (_gate)
        {
            _choices = choices;
            stillCustomizing = _customizing;
        }

        if (stillCustomizing)
        {
            Push(includeTemplate: false);
        }
    }

    private void SaveCustomization(string? value)
    {
        lock (_gate)
        {
            var scope = value == EverythingChoice || value is null
                ? Scope.Everything
                : _choices?.FirstOrDefault(choice => choice.Value == value)?.Scope ?? _scope;
            _scope = scope;
            CustomState = scope.Write();
        }

        Log.Info($"Vercel card {Id} now shows {Scope.Read(CustomState).Label}");
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
