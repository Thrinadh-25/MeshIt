using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using meshIt.Data;
using meshIt.Models;
using meshIt.Services;
using Microsoft.Win32;
using Serilog;

namespace meshIt.ViewModels;

/// <summary>
/// Main view model — orchestrates Phase 1 + 2 + 3 services.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    // ---- Phase 1 services ----
    private readonly SettingsService _settings;
    private readonly BleAdvertiser _advertiser;
    private readonly BleScanner _scanner;
    private readonly GattServerService _gattServer;
    private readonly BleConnectionManager _connectionManager;
    private readonly MessageService _messageService;
    private readonly FileTransferService _fileTransferService;
    private readonly Dispatcher _dispatcher;

    // ---- Phase 2 services ----
    private readonly IdentityService _identityService;
    private readonly NoiseProtocolService _noiseService;
    private readonly MeshRoutingService _meshRoutingService;
    private readonly StoreForwardService _storeForwardService;
    private readonly ChannelService _channelService;
    private readonly TrustService _trustService;
    private readonly VerificationService _verificationService;
    private readonly MigrationService _migrationService;

    // ---- Phase 3 services ----
    private readonly ThemeService _themeService;
    private readonly LocalizationService _localizationService;
    private readonly NotificationService _notificationService;
    private readonly CrashReporter _crashReporter;
    private readonly AudioService _audioService;
    private readonly BackupService _backupService;
    private readonly ScreenLockService _screenLockService;
    private readonly DispatcherTimer _lockCheckTimer;
    private long _messagesSentCount;
    private long _messagesReceivedCount;

    // ---- Emergency wipe ----
    private DateTime _lastLogoTap;
    private int _logoTapCount;

    // ---- Sub-ViewModels ----
    public ChannelListViewModel ChannelListVm { get; }
    public IdentityViewModel IdentityVm { get; private set; } = null!;
    public DiagnosticsViewModel DiagnosticsVm { get; } = new();
    public SettingsViewModel SettingsVm { get; private set; } = null!;

    // ---- Observable state ----
    public ObservableCollection<Peer> Peers { get; } = new();
    public ObservableCollection<Message> Messages { get; } = new();
    public ObservableCollection<FileTransfer> FileTransfers { get; } = new();

    [ObservableProperty] private Peer? _selectedPeer;
    [ObservableProperty] private string _messageText = string.Empty;
    [ObservableProperty] private string _statusText = "Starting…";
    [ObservableProperty] private bool _isBleAvailable = true;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private bool _isSettingsOpen;
    [ObservableProperty] private bool _isIdentityOpen;
    [ObservableProperty] private bool _isDiagnosticsOpen;
    [ObservableProperty] private bool _isRecordingVoice;
    [ObservableProperty] private bool _isDragOver;

    public string AppVersion => "3.0.0";
    public string ShortFingerprint => _identityService.CurrentIdentity?.ShortFingerprint ?? "—";

    public MainViewModel()
    {
        _dispatcher = Application.Current.Dispatcher;

        // Phase 1 services
        _settings = new SettingsService();
        _settings.Load();

        var db = new AppDbContext();
        db.Database.EnsureCreated();

        _advertiser = new BleAdvertiser();
        _scanner = new BleScanner();
        _gattServer = new GattServerService();
        _connectionManager = new BleConnectionManager();
        _messageService = new MessageService(_connectionManager, _gattServer, db);
        _fileTransferService = new FileTransferService(_connectionManager, _gattServer);

        // Phase 2 services
        _identityService = new IdentityService();
        _noiseService = new NoiseProtocolService(_identityService);
        _trustService = new TrustService();
        _verificationService = new VerificationService();
        _storeForwardService = new StoreForwardService();
        _channelService = new ChannelService(_identityService);
        _meshRoutingService = new MeshRoutingService(_identityService, _connectionManager, _noiseService);
        _migrationService = new MigrationService(_settings, _identityService);

        // Phase 3 services
        _themeService = new ThemeService();
        _localizationService = new LocalizationService();
        _notificationService = new NotificationService();
        _crashReporter = new CrashReporter();
        _audioService = new AudioService();
        _backupService = new BackupService();
        _screenLockService = new ScreenLockService();

        _crashReporter.Initialize();

        // Sub-ViewModels
        ChannelListVm = new ChannelListViewModel(_channelService);
        SettingsVm = new SettingsViewModel(_themeService, _localizationService, _screenLockService, _notificationService);

        Username = _settings.Current.Username;

        // Wire up Phase 1 events
        _scanner.PeerDiscovered += OnPeerDiscovered;
        _scanner.PeerLost += OnPeerLost;
        _connectionManager.Connected += addr => Log.Debug("Connected to {Address}", addr);
        _connectionManager.Disconnected += OnPeerDisconnected;
        _messageService.MessageReceived += OnMessageReceived;
        _fileTransferService.TransferStarted += OnTransferStarted;
        _fileTransferService.TransferProgress += OnTransferProgress;
        _fileTransferService.TransferCompleted += OnTransferCompleted;
        _fileTransferService.TransferFailed += OnTransferFailed;

        // Wire up Phase 2 events
        _noiseService.SessionEstablished += OnNoiseSessionEstablished;
        _meshRoutingService.MessageDelivered += OnRoutedMessageDelivered;

        // Phase 3: lock check timer
        _lockCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _lockCheckTimer.Tick += (_, _) =>
        {
            if (_screenLockService.ShouldLock)
                ShowLockScreen();
        };
        _lockCheckTimer.Start();
    }

    // ============================================================
    // BLE Initialization
    // ============================================================

    [RelayCommand]
    public async Task InitializeBleAsync()
    {
        try
        {
            // Apply theme from settings
            _themeService.ApplyTheme(_settings.Current.Theme);
            _localizationService.ChangeLanguage("en-US");

            // Phase 2: ensure cryptographic identity
            _identityService.LoadOrCreateIdentity(Username);
            _migrationService.MigrateIfNeeded();

            IdentityVm = new IdentityViewModel(_identityService, _verificationService, _trustService);
            OnPropertyChanged(nameof(IdentityVm));
            OnPropertyChanged(nameof(ShortFingerprint));

            // ---- BLE availability check (Phase 3 fix) ----
            StatusText = "Checking Bluetooth…";
            var (bleAvailable, bleMessage) = await BleAvailabilityChecker.CheckAsync();
            if (!bleAvailable)
            {
                IsBleAvailable = false;
                StatusText = $"⚠ {bleMessage}";
                Log.Warning("BLE not available: {Reason}", bleMessage);
                return;
            }

            Log.Information("BLE check passed: {Info}", bleMessage);

            var userId = _settings.Current.UserId;
            _messageService.SetIdentity(userId, Username);
            _fileTransferService.SetIdentity(userId);

            _advertiser.Start(userId, Username);
            _scanner.Start();
            await _gattServer.StartAsync();

            _channelService.JoinChannel("#general");

            IsBleAvailable = true;
            StatusText = $"Online — {ShortFingerprint}";
            Log.Information("meshIt v3 initialized — fingerprint {Fp}", ShortFingerprint);
        }
        catch (Exception ex)
        {
            IsBleAvailable = false;
            StatusText = $"⚠ BLE error: {ex.Message}";
            Log.Error(ex, "BLE initialization failed");
        }
    }

    // ============================================================
    // User Commands
    // ============================================================

    [RelayCommand]
    public async Task SetUsernameAsync(string name)
    {
        Username = name;
        _settings.Current.Username = name;
        _settings.Save();
        await InitializeBleAsync();
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(MessageText)) return;
        _screenLockService.RecordActivity();

        // IRC commands
        var (handled, response) = _channelService.ProcessCommand(MessageText.Trim());
        if (handled)
        {
            if (!string.IsNullOrEmpty(response))
            {
                _dispatcher.Invoke(() => Messages.Add(new Message
                {
                    SenderName = "System",
                    Content = response,
                    IsOutgoing = false,
                    Timestamp = DateTime.Now
                }));
            }
            MessageText = string.Empty;
            return;
        }

        if (SelectedPeer is null) return;

        var msg = await _messageService.SendMessageAsync(SelectedPeer, MessageText.Trim());
        if (msg is not null)
        {
            _dispatcher.Invoke(() => Messages.Add(msg));
            _messagesSentCount++;
            MessageText = string.Empty;
        }
    }

    [RelayCommand]
    private async Task SendFileAsync()
    {
        if (SelectedPeer is null) return;
        _screenLockService.RecordActivity();

        var dialog = new OpenFileDialog
        {
            Title = "Select file to send",
            Filter = "All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true) return;
        await _fileTransferService.SendFileAsync(SelectedPeer, dialog.FileName);
    }

    /// <summary>Send files via drag-and-drop.</summary>
    [RelayCommand]
    private async Task SendDroppedFilesAsync(string[] filePaths)
    {
        if (SelectedPeer is null || filePaths is null) return;

        foreach (var file in filePaths)
        {
            await _fileTransferService.SendFileAsync(SelectedPeer, file);
        }
    }

    [RelayCommand]
    private void ToggleVoiceRecording()
    {
        if (_audioService.IsRecording)
        {
            var wavData = _audioService.StopRecording();
            IsRecordingVoice = false;

            if (wavData.Length > 0 && SelectedPeer is not null)
            {
                // Send as a special message with voice data marker
                var msg = new Message
                {
                    SenderName = Username,
                    Content = "🎤 Voice message",
                    IsOutgoing = true,
                    Timestamp = DateTime.Now
                };
                _dispatcher.Invoke(() => Messages.Add(msg));
                _messagesSentCount++;
            }
        }
        else
        {
            _audioService.StartRecording();
            IsRecordingVoice = true;
        }
    }

    [RelayCommand]
    private void ToggleSettings()
    {
        IsSettingsOpen = !IsSettingsOpen;
        IsIdentityOpen = false;
        IsDiagnosticsOpen = false;
    }

    [RelayCommand]
    private void ToggleIdentity()
    {
        IsIdentityOpen = !IsIdentityOpen;
        IsSettingsOpen = false;
        IsDiagnosticsOpen = false;
    }

    [RelayCommand]
    private void ToggleDiagnostics()
    {
        IsDiagnosticsOpen = !IsDiagnosticsOpen;
        IsSettingsOpen = false;
        IsIdentityOpen = false;

        if (IsDiagnosticsOpen)
            DiagnosticsVm.RefreshStats(Peers, _messagesSentCount, _messagesReceivedCount);
    }

    [RelayCommand]
    private void ExportBackup()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Backup",
            Filter = "meshIt Backup (*.meshit-backup)|*.meshit-backup",
            FileName = $"meshit-backup-{DateTime.Now:yyyyMMdd}"
        };

        if (dialog.ShowDialog() == true)
        {
            _backupService.ExportBackup(dialog.FileName, "meshIt-default");
            StatusText = "✅ Backup exported";
        }
    }

    [RelayCommand]
    private void ImportBackup()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Backup",
            Filter = "meshIt Backup (*.meshit-backup)|*.meshit-backup"
        };

        if (dialog.ShowDialog() == true)
        {
            _backupService.ImportBackup(dialog.FileName, "meshIt-default");
            StatusText = "✅ Backup restored — restart recommended";
        }
    }

    [RelayCommand]
    private void ClearData()
    {
        var result = MessageBox.Show(
            "This will permanently delete ALL data including messages, settings, and identity keys.\n\nContinue?",
            "Emergency Wipe", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            _settings.ClearAllData();
            Messages.Clear();
            FileTransfers.Clear();
            StatusText = "🗑️ All data wiped";
        }
    }

    /// <summary>Emergency wipe: triple-tap logo within 2 seconds.</summary>
    public void OnLogoTapped()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastLogoTap).TotalSeconds < 2)
        {
            _logoTapCount++;
            if (_logoTapCount >= 3)
            {
                _logoTapCount = 0;
                ClearDataCommand.Execute(null);
            }
        }
        else
        {
            _logoTapCount = 1;
        }
        _lastLogoTap = now;
    }

    private void ShowLockScreen()
    {
        _dispatcher.Invoke(() =>
        {
            var lockView = new Views.LockScreenView(_screenLockService);
            lockView.Unlocked += () => _screenLockService.RecordActivity();
            lockView.ShowDialog();
        });
    }

    // ============================================================
    // Peer selection → load chat history
    // ============================================================

    partial void OnSelectedPeerChanged(Peer? value)
    {
        Messages.Clear();
        if (value is null) return;

        var history = _messageService.LoadMessages(value.Id);
        foreach (var m in history) Messages.Add(m);
    }

    // ============================================================
    // Phase 1 BLE event handlers
    // ============================================================

    private void OnPeerDiscovered(Peer peer)
    {
        _dispatcher.Invoke(() =>
        {
            var existing = Peers.FirstOrDefault(p => p.Id == peer.Id);
            if (existing is not null)
            {
                existing.Status = peer.Status;
                existing.SignalStrength = peer.SignalStrength;
                existing.LastSeen = peer.LastSeen;
                existing.BluetoothAddress = peer.BluetoothAddress;
            }
            else
            {
                Peers.Add(peer);
                _notificationService.ShowPeerOnlineNotification(peer.Name);
            }
        });
    }

    private void OnPeerLost(ulong addr)
    {
        _dispatcher.Invoke(() =>
        {
            var peer = Peers.FirstOrDefault(p => p.BluetoothAddress == addr);
            if (peer is not null) peer.Status = PeerStatus.Offline;
        });
    }

    private void OnPeerDisconnected(ulong addr)
    {
        _dispatcher.Invoke(() =>
        {
            var peer = Peers.FirstOrDefault(p => p.BluetoothAddress == addr);
            if (peer is not null) peer.Status = PeerStatus.Offline;
        });
    }

    private void OnMessageReceived(Message msg)
    {
        _messagesReceivedCount++;
        _dispatcher.Invoke(() =>
        {
            var sender = Peers.FirstOrDefault(p => p.Id == msg.SenderId);
            if (sender is not null) msg.SenderName = sender.Name;

            if (SelectedPeer is not null && msg.PeerId == SelectedPeer.Id)
                Messages.Add(msg);
            else
                _notificationService.ShowMessageNotification(msg.SenderName, msg.Content, msg.PeerId);
        });
    }

    private void OnTransferStarted(FileTransfer ft) =>
        _dispatcher.Invoke(() => FileTransfers.Add(ft));

    private void OnTransferProgress(FileTransfer ft) =>
        _dispatcher.Invoke(() =>
        {
            var e = FileTransfers.FirstOrDefault(x => x.FileId == ft.FileId);
            if (e is not null)
            {
                e.TransferredChunks = ft.TransferredChunks;
                e.Progress = ft.Progress;
                e.SpeedBytesPerSecond = ft.SpeedBytesPerSecond;
            }
        });

    private void OnTransferCompleted(FileTransfer ft) =>
        _dispatcher.Invoke(() =>
        {
            var e = FileTransfers.FirstOrDefault(x => x.FileId == ft.FileId);
            if (e is not null) { e.Status = TransferStatus.Completed; e.Progress = 100; }
        });

    private void OnTransferFailed(FileTransfer ft, string error) =>
        _dispatcher.Invoke(() =>
        {
            var e = FileTransfers.FirstOrDefault(x => x.FileId == ft.FileId);
            if (e is not null) e.Status = TransferStatus.Failed;
            Log.Warning("Transfer failed: {Error}", error);
        });

    // ============================================================
    // Phase 2 event handlers
    // ============================================================

    private void OnNoiseSessionEstablished(Guid peerId, NoiseSession session)
    {
        Log.Information("Noise session established with {Fp}", session.RemoteShortFingerprint);
        _dispatcher.Invoke(() => StatusText = $"🔒 Encrypted session with {session.RemoteShortFingerprint}");
    }

    private void OnRoutedMessageDelivered(RoutedMessage msg) =>
        Log.Information("Routed message {Id} delivered after {Hops} hops", msg.MessageId, msg.HopCount);

    // ============================================================
    // Dispose
    // ============================================================

    public void Dispose()
    {
        _lockCheckTimer.Stop();
        _audioService.Dispose();
        _advertiser.Dispose();
        _scanner.Dispose();
        _gattServer.Dispose();
        _connectionManager.Dispose();
    }
}
