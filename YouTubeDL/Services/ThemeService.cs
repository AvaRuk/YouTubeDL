using Avalonia;
using Avalonia.Styling;

namespace YouTubeDL.Services;

public static class ThemeService
{
    public static void Apply(string theme)
    {
        if (Application.Current is not { } application)
            return;

        application.RequestedThemeVariant = theme switch
        {
            "dark" => ThemeVariant.Dark,
            "light" => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };
    }
}
