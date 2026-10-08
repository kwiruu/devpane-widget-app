#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property PublishAot=false

// Copies Task Manager's app icon from this PC into src/DevPane.Widgets/Assets/Local, where it replaces the
// System stats card icon in local builds. Assets/Local is gitignored: the icon is Microsoft's artwork, so it
// must not be committed or shipped in a Store build.
//
// Usage, from the repository folder: dotnet run tools/extract-taskmanager-icon.cs

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

const int OutputSize = 64;

string taskManager = Path.Combine(Environment.SystemDirectory, "Taskmgr.exe");
string? repository = FindRepositoryRoot(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? Directory.GetCurrentDirectory());
if (repository is null)
{
    Console.Error.WriteLine("Run this from inside the Dev Pane repository.");
    return 1;
}

// The card has a light and a dark icon. Task Manager's is full colour and reads on either board, so the same
// image is written under both names, replacing both of Dev Pane's.
string outputFolder = Path.Combine(repository, "src", "DevPane.Widgets", "Assets", "Local");
string[] outputs =
[
    Path.Combine(outputFolder, "SystemStats_Icon_Light.png"),
    Path.Combine(outputFolder, "SystemStats_Icon_Dark.png"),
];

// Ask for the largest size so the downscaled result stays sharp.
using Icon? icon = Icon.ExtractIcon(taskManager, 0, 256);
if (icon is null)
{
    Console.Error.WriteLine($"Couldn't read an icon from {taskManager}.");
    return 1;
}

using Bitmap source = icon.ToBitmap();
using var result = new Bitmap(OutputSize, OutputSize, PixelFormat.Format32bppArgb);
using (var graphics = Graphics.FromImage(result))
{
    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
    graphics.DrawImage(source, 0, 0, OutputSize, OutputSize);
}

Directory.CreateDirectory(outputFolder);
foreach (string output in outputs)
{
    result.Save(output, ImageFormat.Png);
    Console.WriteLine($"Saved {output} ({source.Width}px source).");
}

Console.WriteLine("Rebuild Dev Pane to use it.");
return 0;

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
