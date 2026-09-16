using DevPane.Integrations.Claude;
using DevPane.Widgets;
using DevPane.Widgets.Com;

// Claude Code runs "devpane claude-statusline" as its status line after each reply: save the plan limits it passes in,
// print the line, and exit without starting the provider.
if (args is [ClaudeStatusLine.CommandArgument])
{
    try
    {
        // Claude Code reads the line as UTF-8; the console's default code page would garble "·".
        using var output = Console.OpenStandardOutput();
        output.Write(System.Text.Encoding.UTF8.GetBytes(ClaudeStatusLine.Run(Console.OpenStandardInput())));
    }
    catch (Exception e)
    {
        Log.Error("Claude Code status line failed", e);
    }

    return;
}

WinRT.ComWrappersSupport.InitializeComWrappers();

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    Log.Error("Unhandled exception", e.ExceptionObject as Exception);

Log.Info($"Provider starting (args: '{string.Join(' ', args)}')");

uint cookie = ComServer.Register(Guid.Parse(WidgetProvider.Clsid), new WidgetProviderFactory());

// Stay alive until the last Dev Pane card is removed; Windows starts the provider again when needed.
// This works for both launch paths: Windows starting it for the Widgets Board, and Visual Studio for debugging.
WidgetProvider.NoCardsLeft.WaitOne();

ComServer.Revoke(cookie);
Log.Info("Provider exiting");
