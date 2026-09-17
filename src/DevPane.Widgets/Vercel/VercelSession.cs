using DevPane.Integrations.Vercel;

namespace DevPane.Widgets.Vercel;

internal enum VercelConnectionState
{
    Disconnected,
    Checking,
    Connected,
}

/// <param name="Notice">Why the last attempt to connect ended, or why the token stopped working.</param>
internal sealed record VercelSessionView(VercelConnectionState State, string? Notice);

/// <summary>
/// The Vercel account shared by every Vercel card: the pasted access token, kept in Windows Credential Locker, and the API
/// client that uses it.
/// </summary>
internal static class VercelSession
{
    private static readonly object Gate = new();
    private static VercelConnectionState _state;
    private static string? _notice;
    private static VercelClient? _client;

    static VercelSession()
    {
        if (TokenVault.Vercel.Load() is { } token)
        {
            _client = new VercelClient(token);
            _state = VercelConnectionState.Connected;
        }
    }

    /// <summary>Raised when the connection changes. Handlers run on a background thread.</summary>
    public static event Action? Changed;

    public static VercelClient? Client
    {
        get
        {
            lock (Gate)
            {
                return _client;
            }
        }
    }

    public static VercelSessionView View
    {
        get
        {
            lock (Gate)
            {
                return new VercelSessionView(_state, _notice);
            }
        }
    }

    /// <summary>Checks the token with Vercel, then saves it. The token is never logged or shown again.</summary>
    public static async Task ConnectAsync(string token)
    {
        token = token.Trim();
        if (token.Length == 0)
        {
            Update(VercelConnectionState.Disconnected, "Paste a token first.", client: null);
            return;
        }

        Update(VercelConnectionState.Checking, notice: null, client: null);
        var client = new VercelClient(token);
        try
        {
            await client.VerifyAsync(CancellationToken.None);
            TokenVault.Vercel.Save(token);
            Log.Info("Connected to Vercel");
            Update(VercelConnectionState.Connected, notice: null, client);
        }
        catch (VercelUnauthorizedException)
        {
            Update(VercelConnectionState.Disconnected, "Vercel didn't accept that token. Check that you copied all of it.", client: null);
        }
        catch (Exception e) when (e is VercelException or HttpRequestException or TaskCanceledException)
        {
            Log.Error("Connecting to Vercel failed", e);
            Update(VercelConnectionState.Disconnected, "Couldn't reach Vercel. Try again in a moment.", client: null);
        }
    }

    public static void Disconnect()
    {
        TokenVault.Vercel.Remove();
        Log.Info("Disconnected from Vercel");
        Update(VercelConnectionState.Disconnected, notice: null, client: null);
    }

    /// <summary>Called when Vercel rejects the saved token, for example after it expires.</summary>
    public static void HandleUnauthorized(VercelClient client)
    {
        lock (Gate)
        {
            // Another card may already have handled it, or a new token may have replaced this one.
            if (!ReferenceEquals(_client, client))
            {
                return;
            }
        }

        TokenVault.Vercel.Remove();
        Log.Info("Vercel rejected the saved token");
        Update(VercelConnectionState.Disconnected, "Your Vercel token stopped working, maybe because it expired. Paste a new one.", client: null);
    }

    private static void Update(VercelConnectionState state, string? notice, VercelClient? client)
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
