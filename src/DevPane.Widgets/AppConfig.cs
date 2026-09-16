namespace DevPane.Widgets;

/// <summary>
/// Build-wide settings.
/// </summary>
internal static class AppConfig
{
    /// <summary>
    /// Client ID of Dev Pane's GitHub OAuth App, which must have device flow enabled.
    /// Client IDs aren't secret: device flow doesn't use a client secret.
    /// </summary>
    public const string GitHubClientId = "Ov23liA5maZCTCWRaAgK";
}
