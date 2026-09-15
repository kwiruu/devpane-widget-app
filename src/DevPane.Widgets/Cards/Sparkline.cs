using System.Globalization;
using System.Text;

namespace DevPane.Widgets.Cards;

/// <summary>
/// Draws a small line chart as an SVG data URI, since Adaptive Cards have no chart element.
/// </summary>
internal static class Sparkline
{
    private const int Width = 240;
    private const int Height = 48;
    private const int Padding = 2;
    private const string Stroke = "#3B8FD9";

    /// <param name="values">Values from 0 to 100, oldest first.</param>
    public static string ToDataUri(IReadOnlyCollection<double> values)
    {
        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture,
            $"<svg xmlns='http://www.w3.org/2000/svg' width='{Width}' height='{Height}' viewBox='0 0 {Width} {Height}'>");

        if (values.Count > 1)
        {
            double step = (Width - 2 * Padding) / (double)(values.Count - 1);
            var points = values.Select((value, i) => string.Create(CultureInfo.InvariantCulture,
                $"{Padding + i * step:0.#},{Height - Padding - value / 100 * (Height - 2 * Padding):0.#}"));

            svg.Append(CultureInfo.InvariantCulture,
                $"<polyline fill='none' stroke='{Stroke}' stroke-width='2' stroke-linejoin='round' points='{string.Join(' ', points)}'/>");
        }

        svg.Append("</svg>");
        return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg.ToString()));
    }
}
