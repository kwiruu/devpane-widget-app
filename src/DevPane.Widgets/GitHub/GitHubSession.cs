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
/// </summary>
internal static class GitHubSession
{
    // "repo" is the only OAuth scope that covers private repositories; "read:org" lists organization repositories.
    private const string Scopes = "repo read:org";

    private static readonly object Gate = new();
    private static GitHubSignInState _state;
    private static DeviceCode? _pendingCode;
    private static string? _notice;
    private static string? _login;
    private static GitHubClient? _client;
    private static CancellationTokenSource? _signIn;
    private static Task<bool>? _tokenCheck;

    static GitHubSession()
    {
        if (TokenVault.Load() is { } token)
        {
            _client = new GitHubClient(token);
            _state = GitHubSignInState.SignedIn;
        }
    }

    /// <summary>Raised when the sign-in state changes. Handlers run on a background thread.</summary>
    public static event Action? Changed;

    public static GitHubClient? Client
    {
        get
        {
            lock (Gate)
            {
                return _client;
            }
        }
    }

    public static GitHubSessionView View
    {
        get
        {
            lock (Gate)
            {
                return new GitHubSessionView(_state, _pendingCode, _notice, _login);
            }
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
        TokenVault.Remove();
        EndSession(notice: null);
        Log.Info("GitHub: signed out");
    }

    /// <summary>
    /// Called when GitHub rejects a request with 401. GitHub can do that briefly during incidents, so the token is
    /// checked once more before signing out. Returns true when the user is signed out.
    /// </summary>
    public static Task<bool> HandleUnauthorizedAsync(GitHubClient rejectedClient, GitHubUnauthorizedException rejection)
    {
        lock (Gate)
        {
            if (!ReferenceEquals(_client, rejectedClient))
            {
                // Already signed out, or signed in again with a new token.
                return Task.FromResult(true);
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

            TokenVault.Remove();
            EndSession("GitHub no longer accepts Dev Pane's sign-in. Connect again.");
            Log.Info("GitHub: token confirmed rejected; signed out");
            return true;
        }
        finally
        {
            lock (Gate)
            {
                _tokenCheck = null;
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
                    TokenVault.Save(result.AccessToken!);
                    Update(() =>
                    {
                        _client = new GitHubClient(result.AccessToken!);
                        _state = GitHubSignInState.SignedIn;
                        _pendingCode = null;
                        _notice = null;
                    });
                    Log.Info("GitHub: signed in");
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

    private static void EndSession(string? notice) => Update(() =>
    {
        _signIn = null;
        _client = null;
        _login = null;
        _pendingCode = null;
        _state = GitHubSignInState.SignedOut;
        _notice = notice;
    });

    private static void Update(Action change)
    {
        lock (Gate)
        {
            change();
        }

        Changed?.Invoke();
    }
}
