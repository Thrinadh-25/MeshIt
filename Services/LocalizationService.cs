using System.Windows;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// Manages localization by swapping the language ResourceDictionary.
/// </summary>
public class LocalizationService
{
    public string CurrentLanguage { get; private set; } = "en-US";

    public static readonly string[] SupportedLanguages = { "en-US", "es-ES", "fr-FR" };

    public void ChangeLanguage(string cultureCode)
    {
        try
        {
            var dict = new ResourceDictionary
            {
                Source = new Uri($"Resources/Localization/{cultureCode}.xaml", UriKind.Relative)
            };

            var merged = Application.Current.Resources.MergedDictionaries;
            // Language dict is the second merged dictionary (after theme)
            if (merged.Count > 1) merged.RemoveAt(1);
            merged.Add(dict);

            CurrentLanguage = cultureCode;
            Log.Information("Language changed to {Lang}", cultureCode);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load language {Lang}", cultureCode);
        }
    }

    /// <summary>Get a localized string by key.</summary>
    public static string GetString(string key)
    {
        return Application.Current.TryFindResource(key) as string ?? key;
    }
}
