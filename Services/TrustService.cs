using System.IO;
using System.Text.Json;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// Per-peer trust levels: Unknown → Verified (fingerprint checked out-of-band) → Favorite.
/// Persisted to %APPDATA%\meshIt\trust.json.
/// </summary>
public class TrustService
{
    public enum TrustLevel { Unknown, Verified, Favorite }

    private static readonly string TrustPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "meshIt", "trust.json");

    private Dictionary<string, TrustLevel> _trustDb = new();

    public TrustService()
    {
        Load();
    }

    public TrustLevel GetTrustLevel(string fingerprint) =>
        _trustDb.TryGetValue(fingerprint, out var level) ? level : TrustLevel.Unknown;

    public void SetVerified(string fingerprint)
    {
        _trustDb[fingerprint] = TrustLevel.Verified;
        Save();
        Log.Information("Trust: Marked {Fp} as Verified", fingerprint[..8]);
    }

    public void SetFavorite(string fingerprint)
    {
        _trustDb[fingerprint] = TrustLevel.Favorite;
        Save();
        Log.Information("Trust: Marked {Fp} as Favorite", fingerprint[..8]);
    }

    public void RemoveTrust(string fingerprint)
    {
        _trustDb.Remove(fingerprint);
        Save();
    }

    public IReadOnlyDictionary<string, TrustLevel> GetAll() => _trustDb;

    private void Load()
    {
        try
        {
            if (File.Exists(TrustPath))
            {
                var json = File.ReadAllText(TrustPath);
                _trustDb = JsonSerializer.Deserialize<Dictionary<string, TrustLevel>>(json)
                           ?? new Dictionary<string, TrustLevel>();
            }
        }
        catch (Exception ex) { Log.Warning(ex, "Failed to load trust database"); }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TrustPath)!);
            File.WriteAllText(TrustPath, JsonSerializer.Serialize(_trustDb,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Log.Warning(ex, "Failed to save trust database"); }
    }
}
