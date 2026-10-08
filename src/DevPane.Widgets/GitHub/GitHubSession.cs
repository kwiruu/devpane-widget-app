using DevPane.Integrations.GitHub;

namespace DevPane.Widgets.GitHub;

internal enum GitHubSignInState
{
    SignedOut,
    WaitingForApproval,
    SignedIn,
}

/// <param name="Notice">Why the last sign-in attempt ended, shown on signed-out cards.</param>
internal sealed record GitHubSessionView(GitHubSignInState State, DeviceCode? PendingCode, string? Notice, string? Login);

/// <summary>
/// The GitHub account shared by every GitHub card: device-flow sign-in, the saved token, and the API client.
/// Expiring tokens are renewed shortly before they expire, so a sign-in lasts until six months go by without one.
/// </summary>
internal static class GitHubSession
{
    // "repo" is the only OAuth scope that covers private repositories; "read:org" lists organization repositories.
    private const string Scopes = "repo read:org";

    // Renew this long before the token expires, so a request doesn't start with a token that's about to stop working.
    private static readonly TimeSpan RenewalMargin = TimeSpan.FromMinutes(10);

    private static readonly object Gate = new();
    private static GitHubSignInState _state;
    private static DeviceCode? _pendingCode;
    private static string? _notice;
    private static string? _login;
    private static GitHubToken? _token;
    private static GitHubClient? _client;
    private static CancellationTokenSource? _signIn;
    private static Task<bool>? _tokenCheck;
    private static Task? _renewal;

    static GitHubSession()
    {
        if (PreviewMode.IsActive)
        {
            return;
        }

        if (GitHubTokenStore.Load() is { } token)
        {
            _token = token;
            _client = new GitHubClient(token.AccessToken);
            _state = GitHubSignInState.SignedIn;
        }
    }

    /// <summary>Raised when the sign-in state changes. Handlers run on a background thread.</summary>
    public static event Action? Changed;

    public static GitHubSessionView View
    {
        get
        {
            if (PreviewMode.GitHub is { } preview)
            {
                return preview;
            }

            lock (Gate)
            {
                return new GitHubSessionView(_state, _pendingCode, _notice, _login);
            }
        }
    }

    /// <summary>The client for the signed-in account, or null when signed out. A token about to expire is renewed first.</summary>
    public static async Task<GitHubClient?> GetClientAsync()
    {
        GitHubClient? client;
        lock (Gate)
        {
            client = _client;
            if (client is null
                || _token is not { ExpiresAt: { } expiresAt, RefreshToken: not null }
                || expiresAt - DateTimeOffset.UtcNow > RenewalMargin)
            {
                return client;
            }
        }

        await RenewTokenAsync(client);
        lock (Gate)
        {
            return _client;
        }
    }

    public static void SetLogin(string login)
    {
        lock (Gate)
        {
            _login = login;
        }
    }

    public static void StartSignIn()
    {
        if (string.IsNullOrEmpty(AppConfig.GitHubClientId))
        {
            Update(() => _notice = "GitHub sign-in isn't set up in this build yet: the OAuth App client ID is missing.");
            return;
        }

        CancellationTokenSource signIn;
        lock (Gate)
        {
            if (_state != GitHubSignInState.SignedOut)
            {
                return;
            }

            _signIn = signIn = new CancellationTokenSource();
        }

        _ = RunDeviceFlowAsync(signIn.Token);
    }

    public static void CancelSignIn()
    {
        lock (Gate)
        {
            _signIn?.Cancel();
        }
    }

    public static void SignOut()
    {
        ForgetSignIn(notice: null);
        Log.Info("GitHub: signed out");
    }

    /// <summary>
    /// Called when GitHub rejects a request with 401. GitHub can do that briefly during incidents, so the token is
    /// checked once more before it's renewed or the user is signed out. Returns true when the user is signed out.
    /// </summary>
    public static Task<bool> HandleUnauthorizedAsync(GitHubClient rejectedClient, GitHubUnauthorizedException rejection)
    {
        lock (Gate)
        {
            if (!ReferenceEquals(_client, rejectedClient))
            {
                // Already signed out, or the token was renewed or replaced by a new sign-in since the request started.
                return Task.FromResult(_client is null);
            }

            // Cards refreshing at the same time share one check. Task.Run keeps the check from finishing inside this lock,
            // so its cleanup always happens after the check is stored.
            return _tokenCheck ??= Task.Run(() => CheckRejectedTokenAsync(rejectedClient, rejection));
        }
    }

    private static async Task<bool> CheckRejectedTokenAsync(GitHubClient client, GitHubUnauthorizedException rejection)
    {
        Log.Info($"GitHub: {rejection.Message} Checking the token again.");
        try
        {
            bool valid;
            try
            {
                valid = await client.IsTokenValidAsync(CancellationToken.None);
            }
            catch (Exception e) when (e is GitHubException or HttpRequestException or TaskCanceledException)
            {
                // The check itself failed, so there's no evidence the token is bad. Keep it.
                Log.Error("GitHub: couldn't check the token; keeping the sign-in", e);
                return false;
            }

            if (valid)
            {
                Log.Info("GitHub: the token still works; keeping the sign-in");
                return false;
            }

            bool renewable;
            lock (Gate)
            {
                renewable = _token?.RefreshToken is not null;
            }

            if (renewable)
            {
                Log.Info("GitHub: token confirmed rejected; renewing it");
                await RenewTokenAsync(client);
            }
            else if (ForgetSignIn("GitHub no longer accepts Dev Pane's sign-in. Connect again.", client))
            {
                Log.Info("GitHub: token confirmed rejected; signed out");
            }

            lock (Gate)
            {
                return _client is null;
            }
        }
        finally
        {
            lock (Gate)
            {
                _tokenCheck = null;
            }
        }
    }

    /// <summary>
    /// Renews <paramref name="client"/>'s token. Callers share one renewal, and a token that was already replaced isn't
    /// renewed again: a refresh token works only once, so a second try would be rejected and sign the user out.
    /// </summary>
    private static Task RenewTokenAsync(GitHubClient client)
    {
        lock (Gate)
        {
            if (_renewal is { } running)
            {
                return running;
            }

            if (!ReferenceEquals(_client, client) || _token?.RefreshToken is not { } refreshToken)
            {
                return Task.CompletedTask;
            }

            // Task.Run keeps the renewal from finishing inside this lock, so its cleanup always happens after it's stored.
            return _renewal = Task.Run(() => RunRenewalAsync(client, refreshToken));
        }
    }

    private static async Task RunRenewalAsync(GitHubClient client, string refreshToken)
    {
        try
        {
            GitHubToken? renewed;
            try
            {
                renewed = await GitHubDeviceFlow.RefreshAsync(AppConfig.GitHubClientId, refreshToken, CancellationToken.None);
            }
            catch (Exception e) when (e is GitHubException or HttpRequestException or TaskCanceledException)
            {
                // Nothing says the refresh token is bad, so keep it for the next request to try again.
                Log.Error("GitHub: couldn't renew the token; keeping the sign-in", e);
                return;
            }

            if (renewed is null)
            {
                if (ForgetSignIn("Dev Pane's GitHub sign-in expired. Connect again.", client))
                {
                    Log.Info("GitHub: the refresh token was rejected; signed out");
                }

                return;
            }

            lock (Gate)
            {
                if (!ReferenceEquals(_client, client))
                {
                    // Signed out while renewing. Saving the token would sign back in on the next start.
                    return;
                }

                _token = renewed;
                _client = new GitHubClient(renewed.AccessToken);
                GitHubTokenStore.Save(renewed);
            }

            Log.Info("GitHub: renewed the token");
        }
        finally
        {
            lock (Gate)
            {
                _renewal = null;
            }
        }
    }

    private static async Task RunDeviceFlowAsync(CancellationToken cancellationToken)
    {
        try
        {
            var code = await GitHubDeviceFlow.RequestCodeAsync(AppConfig.GitHubClientId, Scopes, cancellationToken);
            Update(() =>
            {
                _state = GitHubSignInState.WaitingForApproval;
                _pendingCode = code;
                _notice = null;
            });
            Log.Info("GitHub: waiting for the user to approve sign-in");

            var result = await GitHubDeviceFlow.WaitForTokenAsync(AppConfig.GitHubClientId, code, cancellationToken);
            switch (result.Outcome)
            {
                case DeviceFlowOutcome.Approved:
                    var token = result.Token!;
                    GitHubTokenStore.Save(token);
                    Update(() =>
                    {
                        _token = token;
                        _client = new GitHubClient(token.AccessToken);
                        _state = GitHubSignInState.SignedIn;
                        _pendingCode = null;
                        _notice = null;
                    });
                    Log.Info(token.RefreshToken is null ? "GitHub: signed in" : "GitHub: signed in with a token that's renewed before it expires");
                    break;
                case DeviceFlowOutcome.Expired:
                    EndSession("The code expired before it was entered. Try again.");
                    break;
                case DeviceFlowOutcome.Denied:
                    EndSession("Sign-in was canceled on GitHub.");
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            EndSession(notice: null);
        }
        catch (GitHubException e)
        {
            Log.Error("GitHub sign-in failed", e);
            EndSession(e.Message);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            Log.Error("GitHub sign-in failed", e);
            EndSession("Couldn't reach GitHub. Check your connection and try again.");
        }
    }

    /// <summary>Deletes the saved token and signs out.</summary>
    /// <param name="expected">When set, signs out only if this client is still signed in, not a renewed or new one.</param>
    /// <returns>False when <paramref name="expected"/> was no longer signed in.</returns>
    private static bool ForgetSignIn(string? notice, GitHubClient? expected = null)
    {
        lock (Gate)
        {
            if (expected is not null && !ReferenceEquals(_client, expected))
            {
                return false;
            }

            // Inside the lock, so a renewal finishing at the same moment can't save the token again.
            GitHubTokenStore.Remove();
            ClearSession(notice);
        }

        Changed?.Invoke();
        return true;
    }

    private static void EndSession(string? notice) => Update(() => ClearSession(notice));

    private static void ClearSession(string? notice)
    {
        _signIn = null;
        _token = null;
        _client = null;
        _login = null;
        _pendingCode = null;
        _state = GitHubSignInState.SignedOut;
        _notice = notice;
    }

    private static void Update(Action change)
    {
        lock (Gate)
        {
            change();
        }

        Changed?.Invoke();
    }
}
