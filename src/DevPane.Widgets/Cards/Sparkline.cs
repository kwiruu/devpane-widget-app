using System.Globalization;
using System.Text;

namespace DevPane.Widgets.Cards;

/// <summary>
/// Draws a small line chart with a soft fill as an SVG data URI, since Adaptive Cards have no chart element.
/// </summary>
internal static class Sparkline
{
    private const double Width = 100;
    private const double Padding = 1.5;

    /// <param name="values">Samples, oldest first.</param>
    /// <param name="color">Line color; mid-tone colors read on both light and dark cards.</param>
    /// <param name="height">Height in the same units as the fixed width of 100, so it sets the aspect ratio.</param>
    /// <param name="max">
    /// Top of the scale, such as 100 for percentages. Null scales to the largest sample, but never below
    /// <paramref name="minimumMax"/>, so an idle line stays low instead of filling the chart.
    /// </param>
    public static string ToDataUri(IReadOnlyList<double> values, string color, int height, double? max, double minimumMax = 1)
    {
        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture,
            $"<svg xmlns='http://www.w3.org/2000/svg' width='{Width}' height='{height}' viewBox='0 0 {Width} {height}'>");

        if (values.Count > 1)
        {
            double top = max ?? Math.Max(values.Max(), minimumMax);
            double step = (Width - 2 * Padding) / (values.Count - 1);

            var points = new StringBuilder();
            for (int i = 0; i < values.Count; i++)
            {
                double x = Padding + i * step;
                double y = height - Padding - Math.Clamp(values[i] / top, 0, 1) * (height - 2 * Padding);
                points.Append(CultureInfo.InvariantCulture, $"{x:0.#},{y:0.#} ");
            }

            string line = points.ToString().TrimEnd();
            double lastX = Padding + (values.Count - 1) * step;
            svg.Append(CultureInfo.InvariantCulture,
                $"<polygon fill='{color}' fill-opacity='0.18' points='{Padding:0.#},{height} {line} {lastX:0.#},{height}'/>");
            svg.Append(CultureInfo.InvariantCulture,
                $"<polyline fill='none' stroke='{color}' stroke-width='1.6' stroke-linejoin='round' stroke-linecap='round' points='{line}'/>");
        }

        svg.Append("</svg>");
        return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg.ToString()));
    }
}
