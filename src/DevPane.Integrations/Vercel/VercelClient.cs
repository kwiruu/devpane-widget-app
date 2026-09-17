using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DevPane.Integrations.Vercel;

/// <summary>
/// Reads deployments, teams, and projects from the Vercel REST API (https://vercel.com/docs/rest-api) with an access
/// token. Team- and project-scoped tokens reach only their own team or project.
/// </summary>
public sealed partial class VercelClient(string token)
{
    private const string BaseUrl = "https://api.vercel.com";
    private const int PageSize = 100;

    private static readonly HttpClient Http = CreateClient();

    /// <summary>Checks that Vercel accepts the token, with the smallest request every token scope allows.</summary>
    public async Task VerifyAsync(CancellationToken cancellationToken)
    {
        using var _ = await GetJsonAsync("/v7/deployments?limit=1", cancellationToken);
    }

    /// <summary>
    /// True for tokens with full account access. Team- and project-scoped tokens can't read user-level resources, so
    /// Vercel refuses them the user's profile.
    /// </summary>
    public async Task<bool> IsFullAccountTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var _ = await GetJsonAsync("/v2/user", cancellationToken);
            return true;
        }
        catch (VercelForbiddenException)
        {
            return false;
        }
    }

    /// <summary>Newest first.</summary>
    /// <param name="teamId">The team to read, or null for the token's default: the personal account, or the team a scoped token belongs to.</param>
    /// <param name="projectId">One project to read, or null for all projects.</param>
    public async Task<IReadOnlyList<VercelDeployment>> GetDeploymentsAsync(
        string? teamId,
        string? projectId,
        bool productionOnly,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = new List<string> { $"limit={limit.ToString(CultureInfo.InvariantCulture)}" };
        AddScope(query, teamId);
        if (projectId is not null)
        {
            query.Add($"projectId={Uri.EscapeDataString(projectId)}");
        }

        if (productionOnly)
        {
            query.Add("target=production");
        }

        using var json = await GetJsonAsync($"/v7/deployments?{string.Join('&', query)}", cancellationToken);
        var deployments = new List<VercelDeployment>();
        foreach (var item in json.RootElement.GetProperty("deployments").EnumerateArray())
        {
            if (ReadDeployment(item) is { } deployment)
            {
                deployments.Add(deployment);
            }
        }

        return deployments;
    }

    /// <summary>The last lines of a deployment's build log, oldest first, without terminal color codes.</summary>
    /// <param name="teamId">The team the deployment belongs to, or null for the token's default.</param>
    public async Task<IReadOnlyList<VercelLogLine>> GetBuildLogAsync(
        string? teamId,
        string deploymentId,
        int lines,
        CancellationToken cancellationToken)
    {
        // Newest first, with room for events that aren't log lines; follow=0 returns what exists now instead of streaming.
        var query = new List<string> { "direction=backward", "follow=0", $"limit={(lines * 4).ToString(CultureInfo.InvariantCulture)}" };
        AddScope(query, teamId);
        using var json = await GetJsonAsync(
            $"/v3/deployments/{Uri.EscapeDataString(deploymentId)}/events?{string.Join('&', query)}", cancellationToken);
        if (json.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var log = new List<VercelLogLine>();
        foreach (var item in json.RootElement.EnumerateArray())
        {
            if (ReadLogLine(item) is { } line)
            {
                log.Add(line);
            }

            if (log.Count == lines)
            {
                break;
            }
        }

        log.Reverse();
        return log;
    }

    /// <summary>
    /// The domains pointing at a deployment right now: custom domains and vercel.app domains, such as the branch's.
    /// The deployment's own URL isn't included.
    /// </summary>
    /// <param name="teamId">The team the deployment belongs to, or null for the token's default.</param>
    public async Task<IReadOnlyList<string>> GetDeploymentAliasesAsync(string? teamId, string deploymentId, CancellationToken cancellationToken)
    {
        var query = new List<string>();
        AddScope(query, teamId);
        string suffix = query.Count > 0 ? "?" + string.Join('&', query) : string.Empty;
        using var json = await GetJsonAsync($"/v2/deployments/{Uri.EscapeDataString(deploymentId)}/aliases{suffix}", cancellationToken);
        return json.RootElement.TryGetProperty("aliases", out var aliases) && aliases.ValueKind == JsonValueKind.Array
            ? aliases.EnumerateArray().Select(alias => GetString(alias, "alias")).Where(alias => alias.Length > 0).ToList()
            : [];
    }

    /// <summary>Teams the token can see; empty for tokens that can't list teams.</summary>
    public async Task<IReadOnlyList<VercelTeam>> GetTeamsAsync(CancellationToken cancellationToken)
    {
        using var json = await GetJsonAsync($"/v2/teams?limit={PageSize}", cancellationToken);
        return json.RootElement.GetProperty("teams").EnumerateArray()
            .Select(team => new VercelTeam(GetString(team, "id"), FirstNonEmpty(GetString(team, "name"), GetString(team, "slug"))))
            .Where(team => team.Id.Length > 0)
            .ToList();
    }

    /// <summary>Projects with their domain, repository, and live deployment, most recently updated first.</summary>
    /// <param name="teamId">The team to list, or null for the token's default.</param>
    public async Task<IReadOnlyList<VercelProject>> GetProjectsAsync(string? teamId, CancellationToken cancellationToken)
    {
        var query = new List<string> { $"limit={PageSize}" };
        AddScope(query, teamId);
        using var json = await GetJsonAsync($"/v10/projects?{string.Join('&', query)}", cancellationToken);
        return json.RootElement.GetProperty("projects").EnumerateArray()
            .Select(ReadProject)
            .Where(project => project.Id.Length > 0)
            .ToList();
    }

    /// <param name="teamId">The team the project belongs to, or null for the token's default.</param>
    public async Task<VercelProject> GetProjectAsync(string? teamId, string projectId, CancellationToken cancellationToken)
    {
        var query = new List<string>();
        AddScope(query, teamId);
        string suffix = query.Count > 0 ? "?" + string.Join('&', query) : string.Empty;
        using var json = await GetJsonAsync($"/v9/projects/{Uri.EscapeDataString(projectId)}{suffix}", cancellationToken);
        return ReadProject(json.RootElement);
    }

    private static void AddScope(List<string> query, string? teamId)
    {
        if (teamId is not null)
        {
            query.Add($"teamId={Uri.EscapeDataString(teamId)}");
        }
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await Http.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }

        var (message, invalidToken) = await ReadErrorAsync(response, cancellationToken);
        throw response.StatusCode switch
        {
            // Vercel answers a wrong, expired, or deleted token with 403 and "invalidToken": true, not 401.
            HttpStatusCode.Unauthorized => new VercelUnauthorizedException(message),
            HttpStatusCode.Forbidden when invalidToken => new VercelUnauthorizedException(message),
            HttpStatusCode.Forbidden => new VercelForbiddenException(message),
            HttpStatusCode.TooManyRequests => new VercelRateLimitException(ReadRateLimitReset(response)),
            _ => new VercelException(message),
        };
    }

    // Vercel errors look like {"error": {"code": "forbidden", "message": "Not authorized", "invalidToken": true}}.
    private static async Task<(string Message, bool InvalidToken)> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string fallback = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (json.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                string message = GetString(error, "message");
                bool invalidToken = error.TryGetProperty("invalidToken", out var flag) && flag.ValueKind == JsonValueKind.True;
                return (message.Length > 0 ? message : fallback, invalidToken);
            }
        }
        catch (JsonException)
        {
        }

        return (fallback, false);
    }

    private static DateTimeOffset ReadRateLimitReset(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-RateLimit-Reset", out var values)
        && long.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : DateTimeOffset.UtcNow.AddMinutes(1);

    private static VercelProject ReadProject(JsonElement project)
    {
        // "link" describes the connected Git repository.
        string? repository = null;
        string? provider = null;
        if (project.TryGetProperty("link", out var link) && link.ValueKind == JsonValueKind.Object
            && GetString(link, "org") is { Length: > 0 } org && GetString(link, "repo") is { Length: > 0 } repo)
        {
            repository = $"{org}/{repo}";
            provider = NullIfEmpty(GetString(link, "type"));
        }

        // "targets.production" is the deployment serving production, with the domains assigned to it.
        VercelDeployment? live = null;
        var domains = new List<string>();
        if (project.TryGetProperty("targets", out var targets) && targets.ValueKind == JsonValueKind.Object
            && targets.TryGetProperty("production", out var production) && production.ValueKind == JsonValueKind.Object)
        {
            live = ReadDeployment(production, isProduction: true);
            if (production.TryGetProperty("alias", out var aliases) && aliases.ValueKind == JsonValueKind.Array)
            {
                domains.AddRange(aliases.EnumerateArray()
                    .Where(alias => alias.ValueKind == JsonValueKind.String)
                    .Select(alias => alias.GetString()!));
            }
        }

        return new VercelProject(
            GetString(project, "id"),
            GetString(project, "name"),
            PickDomain(domains),
            repository,
            provider,
            live);
    }

    // The domain people would recognize: a custom domain before a vercel.app one, without "www." when both exist,
    // and the shortest vercel.app domain otherwise.
    private static string? PickDomain(List<string> domains) =>
        domains
            .OrderBy(domain => domain.EndsWith(".vercel.app", StringComparison.OrdinalIgnoreCase))
            .ThenBy(domain => domain.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            .ThenBy(domain => domain.Length)
            .FirstOrDefault();

    private static VercelDeployment? ReadDeployment(JsonElement item) => ReadDeployment(item, isProduction: false);

    // Deployments from the deployments list use "uid" and "created"; the ones inside projects use "id" and "createdAt".
    private static VercelDeployment? ReadDeployment(JsonElement item, bool isProduction)
    {
        string id = FirstNonEmpty(GetString(item, "uid"), GetString(item, "id"));
        if (id.Length == 0)
        {
            return null;
        }

        // "state" and "readyState" carry the same values; older responses may have only one of them.
        var state = FirstNonEmpty(GetString(item, "state"), GetString(item, "readyState")) switch
        {
            "QUEUED" => VercelDeploymentState.Queued,
            "INITIALIZING" => VercelDeploymentState.Initializing,
            "BUILDING" => VercelDeploymentState.Building,
            "READY" => VercelDeploymentState.Ready,
            "ERROR" => VercelDeploymentState.Error,
            "CANCELED" => VercelDeploymentState.Canceled,
            "BLOCKED" => VercelDeploymentState.Blocked,
            "DELETED" => VercelDeploymentState.Deleted,
            _ => VercelDeploymentState.Unknown,
        };

        // Git details are in "meta", with keys per Git provider.
        string? branch = null;
        string? message = null;
        string? sha = null;
        if (item.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object)
        {
            branch = NullIfEmpty(FirstNonEmpty(
                GetString(meta, "githubCommitRef"), GetString(meta, "gitlabCommitRef"), GetString(meta, "bitbucketCommitRef")));
            message = NullIfEmpty(FirstNonEmpty(
                GetString(meta, "githubCommitMessage"), GetString(meta, "gitlabCommitMessage"), GetString(meta, "bitbucketCommitMessage")));
            sha = NullIfEmpty(FirstNonEmpty(
                GetString(meta, "githubCommitSha"), GetString(meta, "gitlabCommitSha"), GetString(meta, "bitbucketCommitSha")));
        }

        long created = (item.TryGetProperty("created", out var createdValue) || item.TryGetProperty("createdAt", out createdValue))
            && createdValue.ValueKind == JsonValueKind.Number
            && createdValue.TryGetInt64(out long milliseconds)
                ? milliseconds
                : 0;

        string creator = item.TryGetProperty("creator", out var creatorValue) ? GetString(creatorValue, "username") : string.Empty;
        if (item.TryGetProperty("meta", out var gitMeta) && gitMeta.ValueKind == JsonValueKind.Object)
        {
            creator = FirstNonEmpty(
                GetString(gitMeta, "githubCommitAuthorLogin"), GetString(gitMeta, "gitlabCommitAuthorLogin"),
                GetString(gitMeta, "bitbucketCommitAuthorName"), creator);
        }

        return new VercelDeployment(
            id,
            GetString(item, "projectId"),
            GetString(item, "name"),
            state,
            isProduction || GetString(item, "target") == "production",
            NullIfEmpty(GetString(item, "url")),
            NullIfEmpty(GetString(item, "inspectorUrl")),
            branch,
            message?.Split('\n', 2)[0].Trim(),
            DateTimeOffset.FromUnixTimeMilliseconds(created),
            NullIfEmpty(creator),
            GetTime(item, "buildingAt"),
            GetTime(item, "ready"),
            NullIfEmpty(GetString(item, "errorMessage")),
            sha);
    }

    // Build events carry their text either at the top level or inside "payload", depending on the event's age.
    private static VercelLogLine? ReadLogLine(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string type = GetString(item, "type");
        if (type is not ("stdout" or "stderr" or "command" or "fatal"))
        {
            return null;
        }

        var body = item.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object ? payload : item;
        string text = TerminalCodes().Replace(GetString(body, "text"), string.Empty).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        long created = item.TryGetProperty("created", out var createdValue) && createdValue.TryGetInt64(out long milliseconds)
            ? milliseconds
            : 0;
        bool isError = type is "stderr" or "fatal" || GetString(item, "level") == "error";
        return new VercelLogLine(DateTimeOffset.FromUnixTimeMilliseconds(created), text.Split('\n')[^1].Trim(), isError);
    }

    private static DateTimeOffset? GetTime(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long milliseconds) && milliseconds > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : null;

    private static string GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(value => value.Length > 0) ?? string.Empty;

    private static string? NullIfEmpty(string value) => value.Length > 0 ? value : null;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) })
        {
            Timeout = TimeSpan.FromSeconds(20),
        };

        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DevPane", "0.1"));
        return client;
    }

    // Terminal color and cursor codes, such as "\u001b[36m", which build tools write into their logs.
    [GeneratedRegex(@"\u001B\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex TerminalCodes();
}
