namespace DevPane.Widgets;

/// <summary>
/// Minimal file log. The provider has no window or console, so this is the main way to see what it did.
/// </summary>
internal static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly string FilePath = ResolvePath();

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}: {exception}");

    private static void Write(string level, string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                File.AppendAllText(FilePath, line);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Logging must never take the provider down.
            }
        }
    }

    private static string ResolvePath()
    {
        string folder;
        try
        {
            folder = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        }
        catch (Exception)
        {
            // No package identity, for example when run outside the MSIX package.
            folder = Path.GetTempPath();
        }

        string path = Path.Combine(folder, "devpane-widgets.log");
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
            {
                File.Delete(path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        return path;
    }
}
