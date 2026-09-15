using DevPane.Widgets;
using DevPane.Widgets.Com;

WinRT.ComWrappersSupport.InitializeComWrappers();

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    Log.Error("Unhandled exception", e.ExceptionObject as Exception);

// Windows passes this argument when the Widgets Board starts the provider.
bool launchedByWindows = args.Contains("-RegisterProcessAsComServer", StringComparer.OrdinalIgnoreCase);
Log.Info($"Provider starting (launched by Windows: {launchedByWindows})");

uint cookie = ComServer.Register(Guid.Parse(WidgetProvider.Clsid), new WidgetProviderFactory());

if (launchedByWindows)
{
    // Stay alive until the last Dev Pane card is removed. Windows starts the provider again when needed.
    WidgetProvider.NoCardsLeft.WaitOne();
}
else
{
    // Started from Visual Studio: keep running so the Widgets Board connects to this debuggable process.
    Thread.Sleep(Timeout.Infinite);
}

ComServer.Revoke(cookie);
Log.Info("Provider exiting");
