#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property PublishAot=false

// Draws the card icons the Widgets Board shows in its + dialog, for the cards whose artwork is Dev Pane's own.
//
// Each icon is a bare glyph on a transparent background, drawn as vector geometry so it stays sharp at every
// size the board asks for, from 16px in a list to 64px in the picker.
//
// GitHub and Jira are not drawn here. GitHub's is the official mark, used under their logo guidelines, and
// Jira's blue tile with a tick is a plain checkmark rather than anyone's logomark. Both ship as committed PNGs.
//
// Most glyphs come in a light and a dark version, because a bare glyph has to carry its own contrast: Dev Pane's
// teal reads at 5.4:1 on the board's light surface but only 2.3:1 on its dark one, so the dark version is the
// lifted tint rather than the same colour. Claude's orange clears both and ships as a single file.
//
// Usage, from the repository folder: dotnet run tools/generate-widget-icons.cs

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// Every glyph is drawn in this coordinate space and scaled to the output size.
const float Box = 100f;
const int OutputSize = 64;

var tealOnLight = ColorTranslator.FromHtml("#0A6A93");
var tealOnDark = ColorTranslator.FromHtml("#4FB3DD");
var claudeOrange = ColorTranslator.FromHtml("#D97757");

// Vercel's brand is monochrome, so its glyph is the board's foreground colour rather than a hue of its own.
var monoOnLight = ColorTranslator.FromHtml("#000000");
var monoOnDark = ColorTranslator.FromHtml("#FFFFFF");

string? repository = FindRepositoryRoot(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? Directory.GetCurrentDirectory());
if (repository is null)
{
    Console.Error.WriteLine("Run this from inside the Dev Pane repository.");
    return 1;
}

string folder = Path.Combine(repository, "src", "DevPane.Widgets", "Assets", "Widgets");
Directory.CreateDirectory(folder);

var written = new List<string>();

Save("SystemStats_Icon_Light", tealOnLight, Bars);
Save("SystemStats_Icon_Dark", tealOnDark, Bars);
Save("LocalDev_Icon_Light", tealOnLight, Terminal);
Save("LocalDev_Icon_Dark", tealOnDark, Terminal);
Save("Vercel_Icon_Light", monoOnLight, Globe);
Save("Vercel_Icon_Dark", monoOnDark, Globe);
Save("ClaudeUsage_Icon", claudeOrange, ProgressRing);

// The single-version files these replaced. The manifest now points at the light and dark pair instead.
foreach (string stale in new[] { "SystemStats_Icon.png", "LocalDev_Icon.png" })
{
    string path = Path.Combine(folder, stale);
    if (File.Exists(path))
    {
        File.Delete(path);
        Console.WriteLine($"Removed {stale} (replaced by its light and dark versions).");
    }
}

Console.WriteLine($"Wrote {written.Count} icons to {folder}.");
return 0;

void Save(string name, Color color, Action<Graphics, Color> draw)
{
    using var image = new Bitmap(OutputSize, OutputSize, PixelFormat.Format32bppArgb);
    using (var graphics = Graphics.FromImage(image))
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);
        graphics.ScaleTransform(OutputSize / Box, OutputSize / Box);
        draw(graphics, color);
    }

    string path = Path.Combine(folder, name + ".png");
    image.Save(path, ImageFormat.Png);
    written.Add(path);
}

// Three rising bars: the shape the System stats card has always used, without the tile it used to sit on.
static void Bars(Graphics graphics, Color color)
{
    using var brush = new SolidBrush(color);
    foreach ((float x, float y, float height) in new[] { (14f, 54f, 32f), (41.5f, 32f, 54f), (69f, 14f, 72f) })
    {
        using var path = RoundedRectangle(x, y, 17f, height, 5f);
        graphics.FillPath(brush, path);
    }
}

// A shell prompt: the chevron and the underscore after it.
static void Terminal(Graphics graphics, Color color)
{
    using var pen = Stroke(color, 13f);
    graphics.DrawLines(pen, [new PointF(16f, 24f), new PointF(42f, 50f), new PointF(16f, 76f)]);
    graphics.DrawLine(pen, 54f, 76f, 84f, 76f);
}

// A globe for the Vercel card: the site that's live, rather than Vercel's own mark.
static void Globe(Graphics graphics, Color color)
{
    const float radius = 40f;
    using var pen = Stroke(color, 9f);

    graphics.DrawEllipse(pen, 50f - radius, 50f - radius, radius * 2f, radius * 2f);

    // The meridian, drawn as an ellipse narrow enough to read as the far side of the sphere.
    graphics.DrawEllipse(pen, 50f - 17f, 50f - radius, 34f, radius * 2f);

    // Two parallels, each stopping exactly where it meets the outline.
    foreach (float offset in new[] { -14f, 14f })
    {
        float half = MathF.Sqrt(radius * radius - offset * offset);
        graphics.DrawLine(pen, 50f - half, 50f + offset, 50f + half, 50f + offset);
    }
}

// A ring part-filled: how much of the Claude Code limit is spent. The faint track behind it is what makes it
// read as a proportion instead of a spinner.
static void ProgressRing(Graphics graphics, Color color)
{
    const float radius = 34f;
    const float used = 0.72f;
    var bounds = new RectangleF(50f - radius, 50f - radius, radius * 2f, radius * 2f);

    using (var track = new Pen(Color.FromArgb(64, color), 13f))
    {
        graphics.DrawEllipse(track, bounds);
    }

    using var pen = Stroke(color, 13f);
    graphics.DrawArc(pen, bounds, startAngle: -90f, sweepAngle: 360f * used);
}

// Round caps and joins throughout: the glyphs read as one family because every stroke ends the same way.
static Pen Stroke(Color color, float width) =>
    new(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

static GraphicsPath RoundedRectangle(float x, float y, float width, float height, float radius)
{
    radius = Math.Min(radius, Math.Min(width, height) / 2f);

    float diameter = radius * 2f;
    var path = new GraphicsPath();
    path.AddArc(x, y, diameter, diameter, 180, 90);
    path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
    path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
    path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
    path.CloseFigure();
    return path;
}

static string? FindRepositoryRoot(string start)
{
    for (var folder = new DirectoryInfo(start); folder is not null; folder = folder.Parent)
    {
        if (File.Exists(Path.Combine(folder.FullName, "DevPane.slnx")))
        {
            return folder.FullName;
        }
    }

    return null;
}
