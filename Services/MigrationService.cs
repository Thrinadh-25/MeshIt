using Serilog;

namespace meshIt.Services;

/// <summary>
/// Migrates data from Phase 1 to Phase 2 format.
/// Generates cryptographic identity, updates settings schema.
/// </summary>
public class MigrationService
{
    private readonly SettingsService _settingsService;
    private readonly IdentityService _identityService;

    public MigrationService(SettingsService settingsService, IdentityService identityService)
    {
        _settingsService = settingsService;
        _identityService = identityService;
    }

    /// <summary>
    /// Run migration on startup if needed.
    /// - Preserves existing username as the identity nickname
    /// - Generates cryptographic keypairs if not present
    /// - Updates settings version to 2
    /// </summary>
    public void MigrateIfNeeded()
    {
        var settings = _settingsService.Current;

        // If identity already exists, no migration needed
        if (_identityService.CurrentIdentity is not null)
        {
            Log.Debug("Migration: Identity already exists, skipping");
            return;
        }

        Log.Information("Migration: Migrating from Phase 1 to Phase 2");

        // Generate cryptographic identity, preserving the old username as nickname
        var nickname = string.IsNullOrEmpty(settings.Username) ? "User" : settings.Username;
        _identityService.LoadOrCreateIdentity(nickname);

        // Update settings
        settings.AppVersion = "2.0.0";
        _settingsService.Save();

        Log.Information("Migration: Complete — identity fingerprint {Fp}",
            _identityService.CurrentIdentity!.ShortFingerprint);
    }
}
