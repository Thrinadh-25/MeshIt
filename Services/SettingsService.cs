using System.IO;
using System.Text.Json;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// Persists user settings to %APPDATA%\meshIt\settings.json.
/// </summary>
public sealed class SettingsService
{
    private static readonly string AppDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "meshIt");

    private static readonly string SettingsPath = Path.Combine(AppDataDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>Current in-memory settings.</summary>
    public UserSettings Current { get; private set; } = new();

    /// <summary>
    /// Load settings from disk. If the file does not exist, a fresh
    /// <see cref="UserSettings"/> is returned (first-launch scenario).
    /// </summary>
    public UserSettings Load()
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);

            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                Current = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions) ?? new UserSettings();
                Log.Information("Settings loaded for user '{Username}'", Current.Username);
            }
            else
            {
                Current = new UserSettings();
                Log.Information("No settings file found — first launch");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load settings; using defaults");
            Current = new UserSettings();
        }

        return Current;
    }

    /// <summary>Save the current settings to disk.</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            var json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(SettingsPath, json);
            Log.Information("Settings saved");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save settings");
        }
    }

    /// <summary>Delete all persisted data (settings + database + logs).</summary>
    public void ClearAllData()
    {
        try
        {
            if (Directory.Exists(AppDataDir))
            {
                Directory.Delete(AppDataDir, recursive: true);
                Log.Information("All meshIt data cleared");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to clear data");
        }
    }
}

/// <summary>
/// Serializable user preferences.
/// </summary>
public class UserSettings
{
    public string Username { get; set; } = string.Empty;
    public Guid UserId { get; set; } = Guid.NewGuid();
    public string Theme { get; set; } = "dark";
    public bool AutoStart { get; set; } = false;
    public string AppVersion { get; set; } = "1.0.0";
}
