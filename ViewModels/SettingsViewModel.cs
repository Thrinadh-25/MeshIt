using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using meshIt.Services;

namespace meshIt.ViewModels;

/// <summary>
/// ViewModel for the enhanced settings panel.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ThemeService _themeService;
    private readonly LocalizationService _localizationService;
    private readonly ScreenLockService _lockService;
    private readonly NotificationService _notificationService;

    [ObservableProperty] private string _selectedTheme = "Dark";
    [ObservableProperty] private string _selectedLanguage = "en-US";
    [ObservableProperty] private bool _notificationsEnabled = true;
    [ObservableProperty] private bool _lockEnabled;
    [ObservableProperty] private int _lockTimeout = 5;
    [ObservableProperty] private string _pinInput = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public string[] AvailableThemes { get; } = { "Dark", "Light", "HighContrast" };
    public string[] AvailableLanguages { get; } = LocalizationService.SupportedLanguages;

    public SettingsViewModel(
        ThemeService themeService,
        LocalizationService localizationService,
        ScreenLockService lockService,
        NotificationService notificationService)
    {
        _themeService = themeService;
        _localizationService = localizationService;
        _lockService = lockService;
        _notificationService = notificationService;

        LockEnabled = _lockService.IsLockConfigured;
    }

    partial void OnSelectedThemeChanged(string value) =>
        _themeService.ApplyTheme(value);

    partial void OnSelectedLanguageChanged(string value) =>
        _localizationService.ChangeLanguage(value);

    partial void OnNotificationsEnabledChanged(bool value) =>
        _notificationService.IsEnabled = value;

    [RelayCommand]
    private void SetLockPin()
    {
        if (string.IsNullOrWhiteSpace(PinInput) || PinInput.Length < 4)
        {
            StatusMessage = "PIN must be at least 4 characters";
            return;
        }

        _lockService.SetPin(PinInput);
        _lockService.TimeoutMinutes = LockTimeout;
        LockEnabled = true;
        PinInput = string.Empty;
        StatusMessage = "✅ Screen lock enabled";
    }

    [RelayCommand]
    private void RemoveLock()
    {
        _lockService.RemovePin();
        LockEnabled = false;
        StatusMessage = "🔓 Screen lock removed";
    }
}
