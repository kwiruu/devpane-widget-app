using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DevPane.Integrations.Jira;

/// <summary>
/// Reads issues and projects from the Jira Cloud REST API (https://developer.atlassian.com/cloud/jira/platform/rest/v3/)
/// with an Atlassian account email and API token.
/// </summary>
public sealed class JiraClient(JiraConnection connection)
{
    private const string GatewayBase = "https://api.atlassian.com/ex/jira/";

    // The fields every issue row needs; the search API returns only the fields it's asked for.
    private static readonly string[] IssueFields = ["summary", "status", "issuetype", "priority", "duedate", "updated", "project"];

    private static readonly HttpClient Http = CreateClient();

    public JiraConnection Connection => connection;

    /// <summary>
    /// Checks the email and API token with the site and returns a working connection. Classic API tokens work with the site
    /// directly; scoped API tokens only work through Atlassian's gateway, so that's tried when the site turns the token away.
    /// </summary>
    /// <param name="site">The site as the user typed it: "acme", "acme.atlassian.net", or a URL on the site.</param>
    public static async Task<JiraConnection> ConnectAsync(string site, string email, string token, CancellationToken cancellationToken)
    {
        string host = NormalizeSite(site);
        var direct = new JiraConnection(host, email.Trim(), token.Trim(), $"https://{host}");
        try
        {
            using var _ = await new JiraClient(direct).GetAsync("/rest/api/3/myself", cancellationToken);
            return direct;
        }
        catch (JiraException e) when (e is JiraUnauthorizedException or JiraRequestException)
        {
            string cloudId = await GetCloudIdAsync(host, cancellationToken)
                ?? throw new JiraUnauthorizedException("Jira didn't accept that email and API token.");
            var gateway = direct with { ApiBase = GatewayBase + cloudId };
            using var _ = await new JiraClient(gateway).GetAsync("/rest/api/3/myself", cancellationToken);
            return gateway;
        }
    }

    /// <summary>Issues matching a JQL query, in the query's order.</summary>
    public async Task<JiraSearchResult> SearchAsync(string jql, int maxResults, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["jql"] = jql,
            ["maxResults"] = maxResults,
            ["fields"] = new JsonArray(IssueFields.Select(field => (JsonNode)field).ToArray()),
        };

        using var json = await SendAsync(HttpMethod.Post, "/rest/api/3/search/jql", body, cancellationToken);
        var root = json.RootElement;
        var issues = root.TryGetProperty("issues", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(ReadIssue).OfType<JiraIssue>().ToList()
            : [];

        // The search API pages with a token and doesn't report a total; a next page token means there are more.
        bool hasMore = root.TryGetProperty("nextPageToken", out var next) && next.ValueKind == JsonValueKind.String
            || (root.TryGetProperty("isLast", out var isLast) && isLast.ValueKind == JsonValueKind.False);
        return new JiraSearchResult(issues, hasMore);
    }

    /// <summary>Projects the user can browse, by name.</summary>
    public async Task<IReadOnlyList<JiraProject>> GetProjectsAsync(CancellationToken cancellationToken)
    {
        using var json = await GetAsync("/rest/api/3/project/search?maxResults=100&orderBy=name", cancellationToken);
        return json.RootElement.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray()
                .Select(project => new JiraProject(GetString(project, "key"), GetString(project, "name")))
                .Where(project => project.Key.Length > 0)
                .ToList()
            : [];
    }

    /// <summary>The issue's page on the Jira site.</summary>
    public string IssueUrl(string key) => $"https://{connection.Site}/browse/{Uri.EscapeDataString(key)}";

    /// <summary>A search results page on the Jira site.</summary>
    public string SearchUrl(string jql) => $"https://{connection.Site}/issues/?jql={Uri.EscapeDataString(jql)}";

    // "acme", "acme.atlassian.net", and "https://acme.atlassian.net/jira/your-work" all become "acme.atlassian.net".
    private static string NormalizeSite(string site)
    {
        string value = site.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Host.Length > 0)
        {
            value = url.Host;
        }

        value = value.Split('/', 2)[0].Trim().ToLowerInvariant();
        return value.Contains('.', StringComparison.Ordinal) ? value : $"{value}.atlassian.net";
    }

    // Every Jira Cloud site publishes its cloud ID, which scoped API tokens need, without authentication.
    private static async Task<string?> GetCloudIdAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Http.GetAsync($"https://{host}/_edge/tenant_info", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return GetString(json.RootElement, "cloudId") is { Length: > 0 } cloudId ? cloudId : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private Task<JsonDocument> GetAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, path, body: null, cancellationToken);

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, connection.ApiBase + path);
        string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{connection.Email}:{connection.Token}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        using var response = await Http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }

        // Without accepted credentials, Jira can redirect to its login page instead of answering 401.
        if ((int)response.StatusCode is >= 300 and < 400)
        {
            throw new JiraUnauthorizedException("Jira didn't accept that email and API token.");
        }

        string message = await ReadErrorAsync(response, cancellationToken);
        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new JiraUnauthorizedException(message),
            HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.NotFound => new JiraRequestException(message),
            HttpStatusCode.TooManyRequests => new JiraRateLimitException(ReadRetryAt(response)),
            _ => new JiraException(message),
        };
    }

    // Jira errors look like {"errorMessages": ["..."], "errors": {"field": "..."}}.
    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = json.RootElement;
            if (root.TryGetProperty("errorMessages", out var messages) && messages.ValueKind == JsonValueKind.Array
                && messages.EnumerateArray().FirstOrDefault(message => message.ValueKind == JsonValueKind.String) is { ValueKind: JsonValueKind.String } first)
            {
                return first.GetString()!;
            }

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object
                && errors.EnumerateObject().FirstOrDefault(error => error.Value.ValueKind == JsonValueKind.String) is { Value.ValueKind: JsonValueKind.String } error)
            {
                return error.Value.GetString()!;
            }

            if (GetString(root, "message") is { Length: > 0 } text)
            {
                return text;
            }
        }
        catch (JsonException)
        {
        }

        return $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
    }

    private static DateTimeOffset ReadRetryAt(HttpResponseMessage response) =>
        response.Headers.RetryAfter?.Delta is { } delay
            ? DateTimeOffset.UtcNow + delay
            : response.Headers.RetryAfter?.Date ?? DateTimeOffset.UtcNow.AddMinutes(1);

    private static JiraIssue? ReadIssue(JsonElement issue)
    {
        string key = GetString(issue, "key");
        if (key.Length == 0 || !issue.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string status = string.Empty;
        var category = JiraStatusCategory.ToDo;
        if (fields.TryGetProperty("status", out var statusValue) && statusValue.ValueKind == JsonValueKind.Object)
        {
            status = GetString(statusValue, "name");
            if (statusValue.TryGetProperty("statusCategory", out var statusCategory))
            {
                category = GetString(statusCategory, "key") switch
                {
                    "indeterminate" => JiraStatusCategory.InProgress,
                    "done" => JiraStatusCategory.Done,
                    _ => JiraStatusCategory.ToDo,
                };
            }
        }

        string issueType = string.Empty;
        int hierarchyLevel = 0;
        if (fields.TryGetProperty("issuetype", out var type) && type.ValueKind == JsonValueKind.Object)
        {
            issueType = GetString(type, "name");
            if (type.TryGetProperty("hierarchyLevel", out var level) && level.TryGetInt32(out int value))
            {
                hierarchyLevel = value;
            }
            else if (type.TryGetProperty("subtask", out var subtask) && subtask.ValueKind == JsonValueKind.True)
            {
                hierarchyLevel = -1;
            }
        }

        string? priority = fields.TryGetProperty("priority", out var priorityValue) && GetString(priorityValue, "name") is { Length: > 0 } name
            ? name
            : null;

        DateOnly? due = DateOnly.TryParseExact(GetString(fields, "duedate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

        // Jira timestamps look like "2026-09-16T14:02:11.000+0800": the offset needs a colon before .NET can read it.
        string updatedText = GetString(fields, "updated");
        DateTimeOffset? updated = updatedText.Length > 5
            && DateTimeOffset.TryParse(updatedText.Insert(updatedText.Length - 2, ":"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : null;

        string projectKey = string.Empty;
        string projectName = string.Empty;
        if (fields.TryGetProperty("project", out var project) && project.ValueKind == JsonValueKind.Object)
        {
            projectKey = GetString(project, "key");
            projectName = GetString(project, "name");
        }

        return new JiraIssue(key, GetString(fields, "summary"), status, category, issueType, hierarchyLevel, priority, due, updated, projectKey, projectName);
    }

    private static string GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10), AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(20),
        };

        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DevPane", "0.1"));
        return client;
    }
}
