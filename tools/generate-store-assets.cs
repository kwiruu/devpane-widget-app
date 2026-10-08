#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property PublishAot=false

// Draws the Dev Pane logo into every package image the Microsoft Store and Windows ask for, at every scale.
//
// The logo is three rounded rectangles, so it's drawn as vector geometry rather than resampled from a PNG:
// a 600px scale-400 tile stays as crisp as the 44px one, which resampling from the old 150px art could not do.
// The proportions below were measured from the original Assets/Square150x150Logo.png, so the output matches
// the artwork that shipped before.
//
// Filenames carry MRT scale and target-size qualifiers ("StoreLogo.scale-200.png"). Windows picks the right
// one at runtime from the unqualified path the manifest asks for ("Assets\StoreLogo.png"), which is why no
// unqualified file is written. Don't add one back: makepri then has to choose between a neutral and a
// qualified candidate for the same resource.
//
// Usage, from the repository folder: dotnet run tools/generate-store-assets.cs

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// Fractions of the image's width, measured from the original 150px artwork.
const float CornerRadius = 0.20f;      // Of the black rounded square behind everything.
const float ContentTop = 37f / 150f;   // The bar and the pane share a top edge and a height.
const float ContentHeight = 76f / 150f;
const float BarLeft = 27f / 150f;
const float BarWidth = 30f / 150f;
const float BarRadius = 15f / 150f;    // Half the bar's width: the ends are semicircles.
const float PaneLeft = 57f / 150f;     // Flush against the bar; only their corners pull them apart.
const float PaneWidth = 66f / 150f;
const float PaneRadius = 22f / 150f;

var background = Color.FromArgb(0, 0, 0);
var bar = Color.FromArgb(217, 217, 217);
var pane = Color.FromArgb(255, 255, 255);

// The scales Windows ships assets at, as percentages. 400 covers the largest tiles on high-DPI displays.
int[] scales = [100, 125, 150, 200, 400];

// The list sizes Windows draws the 44x44 logo at. 256 is used by the Store listing and file dialogs.
int[] targetSizes = [16, 24, 32, 48, 256];

string? repository = FindRepositoryRoot(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? Directory.GetCurrentDirectory());
if (repository is null)
{
    Console.Error.WriteLine("Run this from inside the Dev Pane repository.");
    return 1;
}

string assets = Path.Combine(repository, "src", "DevPane.Widgets", "Assets");
Directory.CreateDirectory(assets);

var written = new List<string>();

// The three logos the package manifest names, at each scale.
foreach ((string name, int baseSize) in new[] { ("Square150x150Logo", 150), ("Square44x44Logo", 44), ("StoreLogo", 50) })
{
    foreach (int scale in scales)
    {
        // Windows rounds scaled asset sizes up, so 50px at 125% is 63px, not 62.5px.
        int size = (int)Math.Ceiling(baseSize * scale / 100.0);
        Save(Path.Combine(assets, $"{name}.scale-{scale}.png"), size);
    }
}

// The app list icon also has fixed-size variants. "altform-unplated" is the one Windows draws without its
// own background plate; the logo already has a background, so the same image serves both.
foreach (int size in targetSizes)
{
    Save(Path.Combine(assets, $"Square44x44Logo.targetsize-{size}.png"), size);
    Save(Path.Combine(assets, $"Square44x44Logo.altform-unplated_targetsize-{size}.png"), size);
}

// The icon the Widgets Board shows for Dev Pane itself, named by ProviderIcons in the manifest. It's the same
// logo, but it has to be a real file at that exact path: the board opens the path it's given instead of asking
// the resource system for it, so the scale-qualified names above are invisible to it.
Save(Path.Combine(assets, "Widgets", "DevPane_Icon.png"), 64);

// The unqualified files from before the scales existed would now compete with them inside makepri.
foreach (string stale in new[] { "Square150x150Logo.png", "Square44x44Logo.png", "StoreLogo.png" })
{
    string path = Path.Combine(assets, stale);
    if (File.Exists(path))
    {
        File.Delete(path);
        Console.WriteLine($"Removed {stale} (replaced by its scale-qualified versions).");
    }
}

Console.WriteLine($"Wrote {written.Count} images to {assets}.");
return 0;

void Save(string path, int size)
{
    using var image = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (var graphics = Graphics.FromImage(image))
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);

        using (var brush = new SolidBrush(background))
        {
            graphics.FillPath(brush, RoundedRectangle(0, 0, size, size, CornerRadius * size));
        }

        float top = ContentTop * size;
        float height = ContentHeight * size;

        using (var brush = new SolidBrush(bar))
        {
            graphics.FillPath(brush, RoundedRectangle(BarLeft * size, top, BarWidth * size, height, BarRadius * size));
        }

        using (var brush = new SolidBrush(pane))
        {
            graphics.FillPath(brush, RoundedRectangle(PaneLeft * size, top, PaneWidth * size, height, PaneRadius * size));
        }
    }

    image.Save(path, ImageFormat.Png);
    written.Add(path);
}

static GraphicsPath RoundedRectangle(float x, float y, float width, float height, float radius)
{
    // A radius past half the shorter side would make the corner arcs overlap; the bar's ends sit exactly there.
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
