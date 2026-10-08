using System.Text.Json;
using System.Text.Json.Nodes;
using DevPane.Integrations.Jira;

namespace DevPane.Widgets.Jira;

internal enum JiraConnectionState
{
    Disconnected,
    Checking,
    Connected,
}

/// <param name="Notice">Why the last attempt to connect ended, or why the saved token stopped working.</param>
/// <param name="Site">The connected site, such as "acme.atlassian.net", or null when disconnected.</param>
internal sealed record JiraSessionView(JiraConnectionState State, string? Notice, string? Site);

/// <summary>
/// The Jira site shared by every Jira card: the site, email, and API token, kept together in Windows Credential Locker,
/// and the API client that uses them.
/// </summary>
internal static class JiraSession
{
    private static readonly object Gate = new();
    private static JiraConnectionState _state;
    private static string? _notice;
    private static JiraClient? _client;

    static JiraSession()
    {
        if (PreviewMode.IsActive)
        {
            return;
        }

        if (Load() is { } connection)
        {
            _client = new JiraClient(connection);
            _state = JiraConnectionState.Connected;
        }
    }

    /// <summary>Raised when the connection changes. Handlers run on a background thread.</summary>
    public static event Action? Changed;

    public static JiraClient? Client
    {
        get
        {
            if (PreviewMode.JiraClient is { } preview)
            {
                return preview;
            }

            lock (Gate)
            {
                return _client;
            }
        }
    }

    public static JiraSessionView View
    {
        get
        {
            if (PreviewMode.Jira is { } preview)
            {
                return preview;
            }

            lock (Gate)
            {
                return new JiraSessionView(_state, _notice, _client?.Connection.Site);
            }
        }
    }

    /// <summary>Checks the site, email, and API token with Jira, then saves them. The token is never logged or shown again.</summary>
    public static async Task ConnectAsync(string site, string email, string token)
    {
        if (string.IsNullOrWhiteSpace(site) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
        {
            Update(JiraConnectionState.Disconnected, "Fill in your site, email, and API token.", client: null);
            return;
        }

        Update(JiraConnectionState.Checking, notice: null, client: null);
        try
        {
            var connection = await JiraClient.ConnectAsync(site, email, token, CancellationToken.None);
            TokenVault.Jira.Save(new JsonObject
            {
                ["site"] = connection.Site,
                ["email"] = connection.Email,
                ["token"] = connection.Token,
                ["apiBase"] = connection.ApiBase,
            }.ToJsonString());
            Log.Info($"Connected to Jira at {connection.Site}");
            Update(JiraConnectionState.Connected, notice: null, new JiraClient(connection));
        }
        catch (JiraUnauthorizedException)
        {
            Update(JiraConnectionState.Disconnected, "Jira didn't accept that email and API token.", client: null);
        }
        catch (Exception e) when (e is JiraException or JsonException)
        {
            Log.Error("Connecting to Jira failed", e);
            Update(JiraConnectionState.Disconnected, "That site didn't answer like Jira Cloud. Check the site name.", client: null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            Log.Error("Connecting to Jira failed", e);
            Update(JiraConnectionState.Disconnected, "Couldn't reach that site. Check the name and try again.", client: null);
        }
    }

    public static void Disconnect()
    {
        TokenVault.Jira.Remove();
        Log.Info("Disconnected from Jira");
        Update(JiraConnectionState.Disconnected, notice: null, client: null);
    }

    /// <summary>Called when Jira rejects the saved token, for example after it expires.</summary>
    public static void HandleUnauthorized(JiraClient client)
    {
        lock (Gate)
        {
            // Another card may already have handled it, or new details may have replaced these.
            if (!ReferenceEquals(_client, client))
            {
                return;
            }
        }

        TokenVault.Jira.Remove();
        Log.Info("Jira rejected the saved API token");
        Update(JiraConnectionState.Disconnected, "Your Jira API token stopped working, maybe because it expired. Connect again.", client: null);
    }

    private static JiraConnection? Load()
    {
        try
        {
            return TokenVault.Jira.Load() is { } saved && JsonNode.Parse(saved) is JsonObject values
                && values["site"]?.GetValue<string>() is { } site
                && values["email"]?.GetValue<string>() is { } email
                && values["token"]?.GetValue<string>() is { } token
                && values["apiBase"]?.GetValue<string>() is { } apiBase
                    ? new JiraConnection(site, email, token, apiBase)
                    : null;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static void Update(JiraConnectionState state, string? notice, JiraClient? client)
    {
        lock (Gate)
        {
            _state = state;
            _notice = notice;
            _client = client;
        }

        Changed?.Invoke();
    }
}
