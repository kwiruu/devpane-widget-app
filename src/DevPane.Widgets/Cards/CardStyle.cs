using System.Globalization;
using System.Text;

namespace DevPane.Widgets.Cards;

/// <summary>How a status is colored, following GitHub's status colors.</summary>
internal enum LabelTone
{
    Neutral,
    Success,
    Attention,
    Danger,
    Accent,
}

/// <summary>
/// Drawing shared by the card templates. Adaptive Cards only offer named colors, so exact colors come from SVG images
/// passed as data URIs, in light and dark variants picked with <see cref="WindowsTheme"/>.
/// </summary>
/// <remarks>
/// The Widgets Board draws SVG images without their <c>text</c> elements, so no image here contains text: words always
/// go in TextBlocks, with images behind or beside them. Background images are scaled to cover, never stretched, and
/// fillMode "repeat" is ignored, so rounded shapes are split into end columns and a flat middle column.
/// </remarks>
internal static class CardStyle
{
    /// <summary>GitHub's color for a tone, or its muted gray for <see cref="LabelTone.Neutral"/>.</summary>
    public static string ToneColor(LabelTone tone, bool light) => (tone, light) switch
    {
        (LabelTone.Success, false) => "#3FB950",
        (LabelTone.Success, true) => "#1A7F37",
        (LabelTone.Danger, false) => "#F85149",
        (LabelTone.Danger, true) => "#D1242F",
        (LabelTone.Attention, false) => "#D29922",
        (LabelTone.Attention, true) => "#9A6700",
        (LabelTone.Accent, false) => "#4493F8",
        (LabelTone.Accent, true) => "#0969DA",
        (_, false) => "#8B949E",
        (_, true) => "#59636E",
    };

    /// <summary>The Adaptive Cards text color closest to a tone.</summary>
    public static string TextColor(LabelTone tone) => tone switch
    {
        LabelTone.Success => "good",
        LabelTone.Danger => "attention",
        LabelTone.Attention => "warning",
        LabelTone.Accent => "accent",
        _ => "default",
    };

    /// <summary>
    /// A tinted, rounded surface behind a tile or pill: images for its left end, middle, and right end columns. The
    /// corner radius is the end width, so ends half the height wide make a fully round pill.
    /// </summary>
    /// <param name="tone">The tint; <see cref="LabelTone.Neutral"/> is a faint gray that suits any card.</param>
    /// <param name="endWidth">Width of the end columns in the template, in pixels.</param>
    /// <param name="height">Height of the columns in the template, in pixels.</param>
    public static (string Left, string Fill, string Right) Surface(LabelTone tone, bool light, int endWidth, int height) =>
        tone == LabelTone.Neutral
            ? Surface(light ? "fill='#1F2328' fill-opacity='0.06'" : "fill='#FFFFFF' fill-opacity='0.07'", endWidth, height)
            : Surface(ToneColor(tone, light), light, endWidth, height);

    /// <summary>A surface tinted with any color, such as a brand color.</summary>
    public static (string Left, string Fill, string Right) Surface(string color, bool light, int endWidth, int height) =>
        Surface(string.Create(CultureInfo.InvariantCulture, $"fill='{color}' fill-opacity='{(light ? 0.12 : 0.18)}'"), endWidth, height);

    /// <summary>A rounded end: a rectangle twice the image's width, with the image showing only its left or right half.</summary>
    /// <param name="paint">SVG fill attributes, such as <c>fill='#2A7AEF'</c>.</param>
    public static string EndImage(string paint, int width, int height, bool left) => DataUri(string.Create(CultureInfo.InvariantCulture,
        $"<svg xmlns='http://www.w3.org/2000/svg' width='{width}' height='{height}'><rect x='{(left ? 0 : -width)}' width='{width * 2}' height='{height}' rx='{width}' {paint}/></svg>"));

    /// <summary>A flat fill for a middle column. Solid, so scaling it to cover any size looks the same.</summary>
    public static string FillImage(string paint) =>
        DataUri($"<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect width='8' height='8' {paint}/></svg>");

    public static string DataUri(string svg) =>
        "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));

    private static (string Left, string Fill, string Right) Surface(string paint, int endWidth, int height) =>
        (EndImage(paint, endWidth, height, left: true), FillImage(paint), EndImage(paint, endWidth, height, left: false));
}
