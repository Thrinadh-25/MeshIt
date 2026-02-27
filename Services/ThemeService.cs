using System.Windows;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// Manages theme switching. Loads ResourceDictionary from Resources/Themes/.
/// Available themes: Dark, Light, HighContrast.
/// </summary>
public class ThemeService
{
    public enum AppTheme { Dark, Light, HighContrast }

    public AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

    /// <summary>Apply a theme by swapping the merged dictionary.</summary>
    public void ApplyTheme(AppTheme theme)
    {
        var themeFile = theme switch
        {
            AppTheme.Light => "Resources/Themes/LightTheme.xaml",
            AppTheme.HighContrast => "Resources/Themes/HighContrastTheme.xaml",
            _ => "Resources/Themes/DarkTheme.xaml"
        };

        var dict = new ResourceDictionary
        {
            Source = new Uri(themeFile, UriKind.Relative)
        };

        var merged = Application.Current.Resources.MergedDictionaries;
        // Remove existing theme dictionary (first one by convention)
        if (merged.Count > 0) merged.RemoveAt(0);
        merged.Insert(0, dict);

        CurrentTheme = theme;
        Log.Information("Theme applied: {Theme}", theme);
    }

    /// <summary>Apply a theme from string name.</summary>
    public void ApplyTheme(string themeName)
    {
        if (Enum.TryParse<AppTheme>(themeName, true, out var theme))
            ApplyTheme(theme);
    }
}
