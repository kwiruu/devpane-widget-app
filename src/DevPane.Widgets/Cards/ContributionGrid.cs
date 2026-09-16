using System.Globalization;
using System.Text;
using DevPane.Integrations.GitHub;

namespace DevPane.Widgets.Cards;

/// <summary>
/// Draws a GitHub-style contribution calendar as an SVG data URI: one column per week (Sunday on top),
/// one square per day, shaded by contribution level. It has no month labels, because the Widgets Board draws
/// SVG images without their text; the card's template captions the grid instead.
/// </summary>
internal static class ContributionGrid
{
    private const int Cell = 10;
    private const int Gap = 3;
    private const int Pitch = Cell + Gap;

    // Days without contributions: a translucent gray that reads on light and dark cards.
    private const string EmptyDay = "fill='#8B949E' fill-opacity='0.22'";

    // GitHub's own calendar greens, from the quietest to the busiest days (index 0 is unused: empty days use EmptyDay).
    private static readonly string[] DarkLevels = ["", "#0E4429", "#006D32", "#26A641", "#39D353"];
    private static readonly string[] LightLevels = ["", "#9BE9A8", "#40C463", "#30A14E", "#216E39"];

    /// <param name="weeks">How many week columns to draw.</param>
    /// <param name="light">Use GitHub's light-mode greens instead of the dark-mode ones.</param>
    /// <param name="skipRecentWeeks">Ends the grid this many weeks before the current week, for a second, older grid.</param>
    public static string ToDataUri(
        IReadOnlyList<ContributionDay> days,
        DateOnly today,
        int weeks,
        bool light,
        int skipRecentWeeks = 0)
    {
        var levels = days.ToDictionary(day => day.Date, day => day.Level);
        string[] colors = light ? LightLevels : DarkLevels;
        var currentWeekStart = today.AddDays(-(int)today.DayOfWeek);
        int width = weeks * Pitch - Gap;
        int height = 7 * Pitch - Gap;

        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture,
            $"<svg xmlns='http://www.w3.org/2000/svg' width='{width}' height='{height}' viewBox='0 0 {width} {height}'>");

        for (int column = 0; column < weeks; column++)
        {
            var weekStart = currentWeekStart.AddDays(-7 * (skipRecentWeeks + weeks - 1 - column));
            int x = column * Pitch;

            for (int row = 0; row < 7; row++)
            {
                var date = weekStart.AddDays(row);
                if (date > today)
                {
                    break;
                }

                int level = Math.Clamp(levels.GetValueOrDefault(date), 0, 4);
                string fill = level == 0 ? EmptyDay : $"fill='{colors[level]}'";
                svg.Append(CultureInfo.InvariantCulture,
                    $"<rect x='{x}' y='{row * Pitch}' width='{Cell}' height='{Cell}' rx='2' {fill}/>");
            }
        }

        svg.Append("</svg>");
        return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg.ToString()));
    }
}
