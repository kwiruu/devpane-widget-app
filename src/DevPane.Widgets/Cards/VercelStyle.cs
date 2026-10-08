using DevPane.Integrations.Vercel;

namespace DevPane.Widgets.Cards;

/// <summary>
/// The Vercel card's look: a globe for the sign-in screen, Vercel's light-on-dark (or dark-on-light) button, project
/// card backgrounds, and status rings in Vercel's status colors, drawn with <see cref="CardStyle"/>.
/// </summary>
internal static class VercelStyle
{
    // Vercel's foreground colors: #EDEDED on dark backgrounds, #171717 on light ones.
    private const string DarkForeground = "#EDEDED";
    private const string LightForeground = "#171717";

    // A globe rather than Vercel's triangle: the card is about the sites you have live, and the triangle is
    // Vercel's trademark. Matches Assets\Widgets\Vercel_Icon_*.png, which tools\generate-widget-icons.cs draws.
    private static readonly string MarkOnDark = MarkImage(DarkForeground);
    private static readonly string MarkOnLight = MarkImage(LightForeground);

    // Vercel's status colors for ready, failed, and building deployments.
    private const string Ready = "#50E3C2";
    private const string Error = "#EE0000";
    private const string Building = "#F5A623";

    private static readonly string InitialTileDark = TileImage("#2E2E2E");
    private static readonly string InitialTileLight = TileImage("#EAEAEA");

    private static readonly (string Left, string Fill, string Right) ButtonOnDark = CardStyle.Button(DarkForeground);
    private static readonly (string Left, string Fill, string Right) ButtonOnLight = CardStyle.Button(LightForeground);

    private static readonly (string Left, string Fill, string Right) SmallPrimaryOnDark = SmallButtonImages($"fill='{DarkForeground}'");
    private static readonly (string Left, string Fill, string Right) SmallPrimaryOnLight = SmallButtonImages($"fill='{LightForeground}'");
    private static readonly (string Left, string Fill, string Right) SmallSecondaryOnDark = SmallButtonImages("fill='#FFFFFF' fill-opacity='0.08'");
    private static readonly (string Left, string Fill, string Right) SmallSecondaryOnLight = SmallButtonImages("fill='#000000' fill-opacity='0.06'");

    public static string Mark(bool light) => light ? MarkOnLight : MarkOnDark;

    /// <summary>Vercel's primary button: light gray on dark cards, near-black on light cards.</summary>
    public static (string Left, string Fill, string Right) Button(bool light) => light ? ButtonOnLight : ButtonOnDark;

    /// <summary>The Adaptive Cards text color for the button's label, which sits on the button image.</summary>
    public static string ButtonTextColor(bool light) => light ? "light" : "dark";

    /// <summary>Vercel's secondary text and icon gray.</summary>
    public static string MutedColor(bool light) => light ? "#666666" : "#A1A1A1";

    /// <summary>
    /// The terminal icon's gray. The terminal is near-black in both themes, so this doesn't switch with <see cref="MutedColor"/>.
    /// </summary>
    public const string TerminalIconColor = "#8C8C8C";

    /// <summary>
    /// The background of a project card: darker than the Widgets Board card in dark mode and lightly shaded in light mode,
    /// with 8px corners. Drawn as end and middle columns like every rounded surface; the ends are 16px wide, so they
    /// also pad the card's content.
    /// </summary>
    public static (string Left, string Fill, string Right) ProjectCard(bool light, int height)
    {
        const int endWidth = 16;
        const int radius = 8;
        string paint = light ? "fill='#000000' fill-opacity='0.05'" : "fill='#000000' fill-opacity='0.32'";
        return (
            CardStyle.EndImage(paint, endWidth, height, left: true, radius),
            CardStyle.FillImage(paint),
            CardStyle.EndImage(paint, endWidth, height, left: false, radius));
    }

    /// <summary>
    /// The terminal behind build log lines: near-black in both themes, like a terminal window, with 8px corners and 12px
    /// padding at the sides.
    /// </summary>
    public static (string Left, string Fill, string Right) Terminal(bool light, int height)
    {
        const int endWidth = 12;
        const int radius = 8;
        string paint = light ? "fill='#171717'" : "fill='#0A0A0A'";
        return (
            CardStyle.EndImage(paint, endWidth, height, left: true, radius),
            CardStyle.FillImage(paint),
            CardStyle.EndImage(paint, endWidth, height, left: false, radius));
    }

    /// <summary>
    /// The Adaptive Cards color for text on the terminal. The light theme's "light" is white; the dark theme leaves
    /// "light" out, and its default text is already white.
    /// </summary>
    public static string TerminalTextColor(bool light) => light ? "light" : "default";

    /// <summary>
    /// The color of a small button's icon, matching its label: the card's text color on secondary buttons, and the
    /// opposite on the primary button.
    /// </summary>
    public static string SmallButtonIconColor(bool light, bool primary) => light == primary ? DarkForeground : LightForeground;

    /// <summary>
    /// A 32px button for the row under the build log: Vercel's primary button (light gray on dark cards, near-black on
    /// light cards), or its secondary one, a faint surface that lets the card show through.
    /// </summary>
    public static (string Left, string Fill, string Right) SmallButton(bool light, bool primary) => (light, primary) switch
    {
        (false, true) => SmallPrimaryOnDark,
        (true, true) => SmallPrimaryOnLight,
        (false, false) => SmallSecondaryOnDark,
        (true, false) => SmallSecondaryOnLight,
    };

    /// <summary>The rounded 32px tile behind a project's initial when its site has no icon.</summary>
    public static string InitialTile(bool light) => light ? InitialTileLight : InitialTileDark;

    /// <summary>
    /// A 20px ring for a production deployment's state: a check when ready, a cross on error, a partial ring while
    /// building, and a plain gray ring otherwise.
    /// </summary>
    public static string StatusRing(VercelDeploymentState? state, bool light)
    {
        string gray = light ? "#C9C9C9" : "#5C5C5C";
        string shape = state switch
        {
            VercelDeploymentState.Ready =>
                $"<circle cx='10' cy='10' r='8.5' fill='none' stroke='{Ready}' stroke-width='1.5'/><path d='M6.5 10.2l2.4 2.4 4.6-4.8' fill='none' stroke='{Ready}' stroke-width='1.6' stroke-linecap='round' stroke-linejoin='round'/>",
            VercelDeploymentState.Error =>
                $"<circle cx='10' cy='10' r='8.5' fill='none' stroke='{Error}' stroke-width='1.5'/><path d='M7.3 7.3l5.4 5.4M12.7 7.3l-5.4 5.4' stroke='{Error}' stroke-width='1.6' stroke-linecap='round'/>",
            VercelDeploymentState.Building or VercelDeploymentState.Initializing or VercelDeploymentState.Blocked =>
                $"<circle cx='10' cy='10' r='8.5' fill='none' stroke='{gray}' stroke-width='1.5'/><path d='M10 1.5a8.5 8.5 0 0 1 8.5 8.5' fill='none' stroke='{Building}' stroke-width='1.8' stroke-linecap='round'/><circle cx='10' cy='10' r='2.5' fill='{Building}'/>",
            _ => $"<circle cx='10' cy='10' r='8.5' fill='none' stroke='{gray}' stroke-width='1.5'/>",
        };

        return CardStyle.DataUri($"<svg xmlns='http://www.w3.org/2000/svg' width='20' height='20' viewBox='0 0 20 20'>{shape}</svg>");
    }

    // Rounded ends 6px wide and 32px high, the size of the template's small button columns.
    private static (string Left, string Fill, string Right) SmallButtonImages(string paint) =>
        (CardStyle.EndImage(paint, 6, 32, left: true), CardStyle.FillImage(paint), CardStyle.EndImage(paint, 6, 32, left: false));

    private static string TileImage(string color) =>
        CardStyle.DataUri($"<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><rect width='32' height='32' rx='8' fill='{color}'/></svg>");

    // The parallels stop at 12.53 and 87.47, where a line 14 above or below the centre meets a circle of radius 40.
    private static string MarkImage(string color) =>
        CardStyle.DataUri(
            $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100' fill='none' stroke='{color}' stroke-width='9'>"
            + "<circle cx='50' cy='50' r='40'/>"
            + "<ellipse cx='50' cy='50' rx='17' ry='40'/>"
            + "<path d='M12.53 36h74.94M12.53 64h74.94'/>"
            + "</svg>");
}
