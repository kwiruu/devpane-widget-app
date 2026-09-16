using System.Net.Http.Headers;
using System.Text.Json;

namespace DevPane.Integrations.GitHub;

/// <summary>
/// The HTTP client shared by all GitHub calls, so connections are reused.
/// </summary>
internal static class GitHubHttp
{
    public static HttpClient Client { get; } = CreateClient();

    public static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    /// <summary>A string property, or an empty string when it's missing or null.</summary>
    public static string GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>A timestamp property, or null when it's missing, null, or not a timestamp.</summary>
    public static DateTimeOffset? GetTime(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.TryGetDateTimeOffset(out var time)
            ? time
            : null;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) })
        {
            Timeout = TimeSpan.FromSeconds(20),
        };

        // GitHub rejects API requests without a User-Agent.
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DevPane", "0.1"));
        return client;
    }
}
