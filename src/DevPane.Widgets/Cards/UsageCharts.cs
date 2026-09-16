using System.Globalization;
using System.Text;

namespace DevPane.Widgets.Cards;

/// <summary>Small charts for usage cards, drawn as SVG images without text (the Widgets Board drops SVG text).</summary>
internal static class UsageCharts
{
    // Each bar sits centered in an equal slot, so a row of equal-width template columns lines up under the bars.
    private const int SlotWidth = 40;
    private const int BarWidth = 22;

    /// <summary>
    /// One bar per value, scaled to the largest. The last bar, for today, is solid and the others lighter; empty days
    /// show a short gray stub.
    /// </summary>
    /// <param name="height">Image height. The image is stretched to the card's width, so this sets how tall it looks.</param>
    public static string Bars(IReadOnlyList<double> values, string color, bool light, int height)
    {
        int width = values.Count * SlotWidth;
        double max = values.Count > 0 ? values.Max() : 0;

        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture,
            $"<svg xmlns='http://www.w3.org/2000/svg' width='{width}' height='{height}' viewBox='0 0 {width} {height}'>");
        for (int i = 0; i < values.Count; i++)
        {
            int x = i * SlotWidth + (SlotWidth - BarWidth) / 2;
            if (values[i] <= 0 || max <= 0)
            {
                svg.Append(CultureInfo.InvariantCulture,
                    $"<rect x='{x}' y='{height - 3}' width='{BarWidth}' height='3' rx='1.5' {TrackPaint(light)}/>");
                continue;
            }

            double barHeight = Math.Max(4, values[i] / max * height);
            double opacity = i == values.Count - 1 ? 1 : 0.55;
            svg.Append(CultureInfo.InvariantCulture,
                $"<rect x='{x}' y='{height - barHeight:0.#}' width='{BarWidth}' height='{barHeight:0.#}' rx='3' fill='{color}' fill-opacity='{opacity}'/>");
        }

        svg.Append("</svg>");
        return CardStyle.DataUri(svg.ToString());
    }

    /// <summary>A rounded progress bar, filled from the left to <paramref name="percent"/> (0 to 100).</summary>
    public static string Progress(double percent, string color, bool light)
    {
        const int width = 280;
        const int height = 8;
        double filled = percent <= 0 ? 0 : Math.Max(height, Math.Min(percent, 100) / 100 * width);
        string fill = filled > 0
            ? string.Create(CultureInfo.InvariantCulture, $"<rect width='{filled:0.#}' height='{height}' rx='{height / 2}' fill='{color}'/>")
            : string.Empty;
        return CardStyle.DataUri(string.Create(CultureInfo.InvariantCulture,
            $"<svg xmlns='http://www.w3.org/2000/svg' width='{width}' height='{height}' viewBox='0 0 {width} {height}'><rect width='{width}' height='{height}' rx='{height / 2}' {TrackPaint(light)}/>{fill}</svg>"));
    }

    private static string TrackPaint(bool light) =>
        light ? "fill='#1F2328' fill-opacity='0.10'" : "fill='#FFFFFF' fill-opacity='0.12'";
}
