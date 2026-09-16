using System.Globalization;
using System.Text.Json.Nodes;
using DevPane.Integrations.GitHub;
using DevPane.Widgets.GitHub;
using Microsoft.Windows.Widgets;
using Microsoft.Windows.Widgets.Providers;

namespace DevPane.Widgets.Cards;

/// <summary>
/// Shared behavior for cards that use the GitHub account: sign-in on the card, refresh requests,
/// and refreshing when the account signs in or out.
/// </summary>
internal abstract class GitHubCardBase<TReading> : PollingCard<TReading>
    where TReading : class
{
    private volatile bool _refreshRequested;
    private volatile bool _refreshing;

    protected GitHubCardBase(WidgetContext context, string? customState)
        : base(context, customState)
    {
        GitHubSession.Changed += OnSessionChanged;
    }

    /// <summary>While true, sign-in changes don't update the card, for example during customization.</summary>
    protected virtual bool IgnoreSessionChanges => false;

    public override void Dispose()
    {
        GitHubSession.Changed -= OnSessionChanged;
        base.Dispose();
    }

    public override void OnAction(string verb, string data)
    {
        switch (verb)
        {
            case "signIn":
                GitHubSession.StartSignIn();
                break;
            case "cancelSignIn":
                GitHubSession.CancelSignIn();
                break;
            case "refresh":
                RequestRefresh();
                break;
        }
    }

    /// <summary>Refreshes right away, even if the last refresh was recent, and shows "Refreshing…" until it's done.</summary>
    protected void RequestRefresh()
    {
        _refreshRequested = true;
        _refreshing = true;
        Push(includeTemplate: false);
        RefreshNow();
    }

    protected override void OnSampleTaken() => _refreshing = false;

    /// <summary>True once after <see cref="RequestRefresh"/>, so the next sample skips its freshness check.</summary>
    protected bool ConsumeRefreshRequest()
    {
        bool requested = _refreshRequested;
        _refreshRequested = false;
        return requested;
    }

    /// <summary>True when Windows is in light mode, for picking GitHub's light or dark image colors.</summary>
    protected static bool LightTheme => WindowsTheme.IsLight();

    /// <summary>
    /// Template data every GitHub card starts from: <c>state</c>, the sign-in screens, and the GitHub look
    /// (mark and button images).
    /// </summary>
    protected JsonObject DescribeSignIn(GitHubSessionView session)
    {
        bool small = Size == WidgetSize.Small;
        bool light = LightTheme;
        var data = new JsonObject
        {
            ["state"] = session.State switch
            {
                GitHubSignInState.SignedIn => "signedIn",
                GitHubSignInState.WaitingForApproval => "waiting",
                _ => "signedOut",
            },
            ["notice"] = session.Notice ?? string.Empty,
            ["userCode"] = session.PendingCode?.UserCode ?? string.Empty,
            ["verificationUrl"] = session.PendingCode?.VerificationUri.ToString() ?? "https://github.com/login/device",
            ["expiresAt"] = session.PendingCode is { } code
                ? code.ExpiresAt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)
                : string.Empty,
            // The sign-in screens are centered vertically in this much height. It's a little less than the room below
            // the card's header, because anything taller than the card is cut off.
            ["centerHeight"] = Size switch
            {
                WidgetSize.Small => "0px",
                // Measured from screenshots: about 240px and 380px fit below the header. Centering in more
                // height than the card has makes the board draw nothing at all.
                WidgetSize.Medium => "220px",
                _ => "360px",
            },
            ["mark"] = GitHubStyle.Mark(light),
            ["markSize"] = small ? "32px" : "48px",
            ["titleSize"] = small ? "medium" : "large",
            ["buttonLeft"] = GitHubStyle.ButtonLeft,
            ["buttonFill"] = GitHubStyle.ButtonFill,
            ["buttonRight"] = GitHubStyle.ButtonRight,
            ["codeBox"] = GitHubStyle.CodeBox(light),
        };

        // GitHub codes look like "WDJB-MJHT": each character gets its own box in the template.
        string characters = new((session.PendingCode?.UserCode ?? string.Empty).Where(char.IsAsciiLetterOrDigit).ToArray());
        for (int i = 0; i < 8; i++)
        {
            data[$"code{i}"] = i < characters.Length ? characters[i].ToString() : string.Empty;
        }

        return data;
    }

    /// <summary>The clickable footer line: refresh in progress, the last refresh problem, or when data was updated.</summary>
    protected string DescribeFreshness(DateTimeOffset? updatedAt, string? problem) => (updatedAt, problem) switch
    {
        _ when _refreshing => "Refreshing…",
        (_, { } message) => message,
        ({ } time, _) => $"Updated {Format.RelativeTime(time)}",
        _ => "Loading from GitHub…",
    };

    /// <summary>A user-facing explanation for a failed GitHub call, or null if the exception isn't a GitHub failure.</summary>
    protected static string? DescribeFailure(Exception exception, bool hasEarlierData) => exception switch
    {
        GitHubRateLimitException rateLimit =>
            $"GitHub's rate limit was reached. It resets at {rateLimit.ResetAt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)}.",
        GitHubException or HttpRequestException or TaskCanceledException => hasEarlierData
            ? "Couldn't reach GitHub. Showing the last update."
            : "Couldn't reach GitHub. Trying again in a few minutes.",
        _ => null,
    };

    private void OnSessionChanged()
    {
        try
        {
            if (IgnoreSessionChanges)
            {
                return;
            }

            Push(includeTemplate: false);
            if (GitHubSession.View.State == GitHubSignInState.SignedIn)
            {
                RequestRefresh();
            }
        }
        catch (Exception e)
        {
            Log.Error($"{GetType().Name} update after a GitHub sign-in change failed", e);
        }
    }
}
