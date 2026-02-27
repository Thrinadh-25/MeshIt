using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// Export/import encrypted backup of all user data (messages, settings, identity).
/// Uses AES-256-GCM with PBKDF2-derived key from a user password.
/// </summary>
public class BackupService
{
    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "meshIt");

    /// <summary>
    /// Export all data to an encrypted .meshit-backup file.
    /// </summary>
    public void ExportBackup(string outputPath, string password)
    {
        var backup = new Dictionary<string, string>();

        // Gather all files from appdata
        if (Directory.Exists(AppDataDir))
        {
            foreach (var file in Directory.GetFiles(AppDataDir, "*.*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(AppDataDir, file);
                backup[relative] = Convert.ToBase64String(File.ReadAllBytes(file));
            }
        }

        var json = JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true });
        var plaintext = Encoding.UTF8.GetBytes(json);

        // Derive key from password
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, 100_000, HashAlgorithmName.SHA256, 32);

        // Encrypt with AES-256-CBC
        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();

        var encrypted = aes.EncryptCbc(plaintext, aes.IV);

        // Output: [16 salt] [16 IV] [encrypted data]
        using var outStream = File.Create(outputPath);
        outStream.Write(salt);
        outStream.Write(aes.IV);
        outStream.Write(encrypted);

        Log.Information("Backup exported to {Path} ({Files} files)", outputPath, backup.Count);
    }

    /// <summary>
    /// Import and restore data from an encrypted backup file.
    /// </summary>
    public void ImportBackup(string inputPath, string password)
    {
        var data = File.ReadAllBytes(inputPath);
        if (data.Length < 33) throw new InvalidDataException("Invalid backup file");

        var salt = data[..16];
        var iv = data[16..32];
        var encrypted = data[32..];

        // Derive key
        var key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, 100_000, HashAlgorithmName.SHA256, 32);

        // Decrypt
        using var aes = Aes.Create();
        aes.Key = key;
        var plaintext = aes.DecryptCbc(encrypted, iv);
        var json = Encoding.UTF8.GetString(plaintext);

        var backup = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        if (backup is null) throw new InvalidDataException("Corrupt backup data");

        // Restore files
        foreach (var (relative, base64) in backup)
        {
            var fullPath = Path.Combine(AppDataDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, Convert.FromBase64String(base64));
        }

        Log.Information("Backup imported from {Path} ({Files} files)", inputPath, backup.Count);
    }
}
