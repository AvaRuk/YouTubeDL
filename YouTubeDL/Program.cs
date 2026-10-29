using Avalonia;

namespace YouTubeDL;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (CommandLineRunner.IsCommandLine(args))
            return CommandLineRunner.RunAsync(args).GetAwaiter().GetResult();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
