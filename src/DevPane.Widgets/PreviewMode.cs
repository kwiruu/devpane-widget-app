using DevPane.Integrations.Jira;
using DevPane.Widgets.GitHub;
using DevPane.Widgets.Jira;
using DevPane.Widgets.Vercel;

namespace DevPane.Widgets;

/// <summary>
/// State for drawing the widget picker's preview images with tools\render-previews.ps1. While it's on, the theme and
/// the GitHub, Vercel and Jira sessions report what's set here instead of reading the registry or Credential Locker,
/// so a preview never shows, or even loads, a real account.
/// </summary>
/// <remarks>
/// Only the render-previews command turns this on, and that command only exists in Debug builds. When Windows starts
/// the provider, nothing here is ever set.
/// </remarks>
internal static class PreviewMode
{
    public static bool IsActive { get; private set; }

    public static bool Light { get; set; }

    public static GitHubSessionView? GitHub { get; set; }

    public static VercelSessionView? Vercel { get; set; }

    public static JiraSessionView? Jira { get; set; }

    /// <summary>Jira cards build their links from a client, so the preview needs one, pointed at a made-up site.</summary>
    public static JiraClient? JiraClient { get; set; }

    /// <summary>
    /// Has to run before anything touches a session: the first use of one loads the saved account, and in preview mode
    /// they skip that.
    /// </summary>
    public static void Enter() => IsActive = true;
}
