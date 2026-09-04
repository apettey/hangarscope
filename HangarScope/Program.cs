using Avalonia;

namespace HangarScope;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack hooks must run first: they handle install/update/uninstall
        // lifecycle events and exit early during them.
        Velopack.VelopackApp.Build().Run();
        Services.UpdateChecker.Start();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
