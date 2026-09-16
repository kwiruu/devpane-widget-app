using System.Globalization;

namespace DevPane.Widgets.Cards;

/// <summary>
/// GitHub's look for the GitHub cards: the mark, buttons, Octicons, status icons, and the sign-in code boxes, drawn with
/// <see cref="CardStyle"/>. Images that sit on the card's background come in GitHub's light and dark variants.
/// </summary>
internal static class GitHubStyle
{
    private const string PrimaryBlue = "#2A7AEF";

    // The GitHub mark from GitHub's Octicons (mark-github-24), unmodified. GitHub's guidelines allow it in white or black.
    private const string MarkPath =
        "M10.226 17.284c-2.965-.36-5.054-2.493-5.054-5.256 0-1.123.404-2.336 1.078-3.144-.292-.741-.247-2.314.09-2.965.898-.112 2.111.36 2.83 1.01.853-.269 1.752-.404 2.853-.404 1.1 0 1.999.135 2.807.382.696-.629 1.932-1.1 2.83-.988.315.606.36 2.179.067 2.942.72.854 1.101 2 1.101 3.167 0 2.763-2.089 4.852-5.098 5.234.763.494 1.28 1.572 1.28 2.807v2.336c0 .674.561 1.056 1.235.786 4.066-1.55 7.255-5.615 7.255-10.646C23.5 6.188 18.334 1 11.978 1 5.62 1 .5 6.188.5 12.545c0 4.986 3.167 9.12 7.435 10.669.606.225 1.19-.18 1.19-.786V20.63a2.9 2.9 0 0 1-1.078.224c-1.483 0-2.359-.808-2.987-2.313-.247-.607-.517-.966-1.034-1.033-.27-.023-.359-.135-.359-.27 0-.27.45-.471.898-.471.652 0 1.213.404 1.797 1.235.45.651.921.943 1.483.943.561 0 .92-.202 1.437-.719.382-.381.674-.718.944-.943";

    // A button is three columns: rounded ends exactly the column size, and a flat middle that can cover any width.
    // Keep these sizes in sync with the templates' button columns (6px x 36px) and code box columns (28px x 38px).
    private const int ButtonEndWidth = 6;
    private const int ButtonHeight = 36;
    private const int CodeBoxWidth = 28;
    private const int CodeBoxHeight = 38;

    private static readonly string MarkWhite = MarkImage("#FFFFFF");
    private static readonly string MarkBlack = MarkImage("#1F2328");
    private static readonly string CodeBoxDark = CodeBoxImage("#161B22", "#30363D");
    private static readonly string CodeBoxLight = CodeBoxImage("#F6F8FA", "#D1D9E0");

    /// <summary>Rounded left end of GitHub's blue button.</summary>
    public static string ButtonLeft { get; } = CardStyle.EndImage($"fill='{PrimaryBlue}'", ButtonEndWidth, ButtonHeight, left: true);

    /// <summary>Flat middle of the button.</summary>
    public static string ButtonFill { get; } = CardStyle.FillImage($"fill='{PrimaryBlue}'");

    /// <summary>Rounded right end of the button.</summary>
    public static string ButtonRight { get; } = CardStyle.EndImage($"fill='{PrimaryBlue}'", ButtonEndWidth, ButtonHeight, left: false);

    /// <summary>The GitHub mark in white for dark mode, or black for light mode.</summary>
    public static string Mark(bool light) => light ? MarkBlack : MarkWhite;

    /// <summary>One box behind a character of the sign-in code, like GitHub's device authorization page.</summary>
    public static string CodeBox(bool light) => light ? CodeBoxLight : CodeBoxDark;

    /// <summary>A 16px status icon in GitHub's colors: check, cross, or dot.</summary>
    public static string StatusIcon(LabelTone tone, bool light)
    {
        string color = CardStyle.ToneColor(tone, light);
        string shape = tone switch
        {
            LabelTone.Success => $"<circle cx='8' cy='8' r='7' fill='{color}'/><path d='M4.7 8.2l2.1 2.1 4.5-4.6' fill='none' stroke='#FFFFFF' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'/>",
            LabelTone.Danger => $"<circle cx='8' cy='8' r='7' fill='{color}'/><path d='M5.6 5.6l4.8 4.8M10.4 5.6l-4.8 4.8' stroke='#FFFFFF' stroke-width='1.7' stroke-linecap='round'/>",
            LabelTone.Attention => $"<circle cx='8' cy='8' r='6.2' fill='none' stroke='{color}' stroke-width='1.6'/><circle cx='8' cy='8' r='3' fill='{color}'/>",
            _ => $"<circle cx='8' cy='8' r='6.2' fill='none' stroke='{color}' stroke-width='1.6'/>",
        };

        return CardStyle.DataUri($"<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16' viewBox='0 0 16 16'>{shape}</svg>");
    }

    /// <summary>A 16px Octicon in a tone's color, or GitHub's muted gray for <see cref="LabelTone.Neutral"/>.</summary>
    public static string Icon(Octicon icon, LabelTone tone, bool light) => Octicons.Image(icon, CardStyle.ToneColor(tone, light));

    private static string MarkImage(string color) =>
        CardStyle.DataUri($"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24'><path fill='{color}' d='{MarkPath}'/></svg>");

    private static string CodeBoxImage(string fill, string border) => CardStyle.DataUri(string.Create(CultureInfo.InvariantCulture,
        $"<svg xmlns='http://www.w3.org/2000/svg' width='{CodeBoxWidth}' height='{CodeBoxHeight}'><rect x='0.5' y='0.5' width='{CodeBoxWidth - 1}' height='{CodeBoxHeight - 1}' rx='6' fill='{fill}' stroke='{border}'/></svg>"));
}
