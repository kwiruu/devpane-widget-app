using System.Text.Json;
using System.Text.Json.Nodes;
using DevPane.Integrations.GitHub;

namespace DevPane.Widgets.GitHub;

/// <summary>
/// Saves the GitHub token in <see cref="TokenVault.GitHub"/>, with its expiry and refresh token, as JSON.
/// </summary>
internal static class GitHubTokenStore
{
    public static GitHubToken? Load()
    {
        if (TokenVault.GitHub.Load() is not { } saved)
        {
            return null;
        }

        // Earlier versions saved only the access token.
        if (!saved.StartsWith('{'))
        {
            return new GitHubToken(saved, null, null);
        }

        try
        {
            using var json = JsonDocument.Parse(saved);
            var root = json.RootElement;
            string? accessToken = ReadString(root, "accessToken");
            if (accessToken is null)
            {
                Log.Info("GitHub: the saved token has no access token; signing in is needed");
                return null;
            }

            return new GitHubToken(
                accessToken,
                root.TryGetProperty("expiresAt", out var expiresAt)
                && expiresAt.ValueKind == JsonValueKind.String
                && expiresAt.TryGetDateTimeOffset(out var time)
                    ? time
                    : null,
                ReadString(root, "refreshToken"));
        }
        catch (JsonException e)
        {
            Log.Error("GitHub: couldn't read the saved token; signing in is needed", e);
            return null;
        }
    }

    public static void Save(GitHubToken token)
    {
        var json = new JsonObject { ["accessToken"] = token.AccessToken };
        if (token.ExpiresAt is { } expiresAt)
        {
            json["expiresAt"] = expiresAt;
        }

        if (token.RefreshToken is { } refreshToken)
        {
            json["refreshToken"] = refreshToken;
        }

        TokenVault.GitHub.Save(json.ToJsonString());
    }

    public static void Remove() => TokenVault.GitHub.Remove();

    private static string? ReadString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
