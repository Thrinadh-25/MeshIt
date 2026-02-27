using System.IO;
using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// PIN-based screen lock to protect the app when idle.
/// PIN is hashed with SHA-256 + salt and stored in settings.
/// </summary>
public class ScreenLockService
{
    private static readonly string LockFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "meshIt", "lock.dat");

    private DateTime _lastActivity = DateTime.UtcNow;

    /// <summary>Auto-lock timeout in minutes. 0 = disabled.</summary>
    public int TimeoutMinutes { get; set; } = 0;

    /// <summary>Whether a PIN is set.</summary>
    public bool IsLockConfigured => File.Exists(LockFile);

    /// <summary>Whether the lock should be shown right now.</summary>
    public bool ShouldLock =>
        IsLockConfigured && TimeoutMinutes > 0 &&
        (DateTime.UtcNow - _lastActivity).TotalMinutes >= TimeoutMinutes;

    /// <summary>Record user activity (resets the idle timer).</summary>
    public void RecordActivity() => _lastActivity = DateTime.UtcNow;

    /// <summary>Set a new PIN (or change existing one).</summary>
    public void SetPin(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = HashPin(pin, salt);
        var data = new byte[16 + 32];
        Array.Copy(salt, 0, data, 0, 16);
        Array.Copy(hash, 0, data, 16, 32);

        Directory.CreateDirectory(Path.GetDirectoryName(LockFile)!);
        File.WriteAllBytes(LockFile, data);
        Log.Information("Screen lock PIN set");
    }

    /// <summary>Verify a PIN attempt.</summary>
    public bool VerifyPin(string pin)
    {
        if (!File.Exists(LockFile)) return true;

        var data = File.ReadAllBytes(LockFile);
        if (data.Length < 48) return false;

        var salt = data[..16];
        var storedHash = data[16..48];
        var inputHash = HashPin(pin, salt);

        var valid = CryptographicOperations.FixedTimeEquals(storedHash, inputHash);
        if (valid) RecordActivity();
        return valid;
    }

    /// <summary>Remove the PIN lock.</summary>
    public void RemovePin()
    {
        if (File.Exists(LockFile))
            File.Delete(LockFile);
        Log.Information("Screen lock PIN removed");
    }

    private static byte[] HashPin(string pin, byte[] salt)
    {
        var input = salt.Concat(Encoding.UTF8.GetBytes(pin)).ToArray();
        return SHA256.HashData(input);
    }
}
