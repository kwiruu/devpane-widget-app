using Microsoft.Win32;

namespace DevPane.Widgets;

/// <summary>
/// Reads whether Windows apps use light or dark mode. The Widgets Board follows this setting but doesn't pass it to
/// providers, so colors baked into SVG images use it to stay readable.
/// </summary>
internal static class WindowsTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool IsLight()
    {
        if (PreviewMode.IsActive)
        {
            return PreviewMode.Light;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Dark is the Widgets Board default.
            return false;
        }
    }
}
