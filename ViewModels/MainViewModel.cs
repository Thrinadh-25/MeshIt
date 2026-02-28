using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InTheHand.Net;
using meshIt.Data;
using meshIt.Models;
using meshIt.Services;
using Microsoft.Win32;
using Serilog;

namespace meshIt.ViewModels;

/// <summary>
/// Main view model — orchestrates all services including hybrid Bluetooth scanning,
/// multi-hop mesh routing, IRC channels, and protocol-agnostic connections.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    // ---- Core services ----
    private readonly SettingsService _settings;
    private readonly BleAdvertiser _advertiser;
    private readonly HybridScanner _hybridScanner;
    private readonly GattServerService _gattServer;
    private readonly BleConnectionManager _connectionManager;
    private readonly ConnectionFactory _connectionFactory;
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
    private readonly DispatcherTimer _channelAnnounceTimer;
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
    public ObservableCollection<string> CurrentChannelMembers { get; } = new();

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
    [ObservableProperty] private string? _currentChannelName;
    [ObservableProperty] private bool _isChannelMembersVisible;
    [ObservableProperty] private string _capabilitiesText = string.Empty;

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
        _connectionManager = new BleConnectionManager();
        _hybridScanner = new HybridScanner();
        _connectionFactory = new ConnectionFactory(_connectionManager);
        _gattServer = new GattServerService(_connectionManager);
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

        // Wire Phase 1: hybrid scanner → peer list
        _hybridScanner.PeerDiscovered += OnPeerDiscovered;
        _hybridScanner.PeerLost += OnPeerLost;
        _connectionManager.Connected += addr => Log.Debug("RFCOMM connected to {Address}", addr);
        _connectionManager.Disconnected += OnPeerDisconnected;
        _messageService.MessageReceived += OnMessageReceived;
        _fileTransferService.TransferStarted += OnTransferStarted;
        _fileTransferService.TransferProgress += OnTransferProgress;
        _fileTransferService.TransferCompleted += OnTransferCompleted;
        _fileTransferService.TransferFailed += OnTransferFailed;

        _advertiser.IncomingConnection += (client, address) =>
            _connectionManager.RegisterIncomingConnection(client, address);

        // Wire Phase 2: mesh routing
        _noiseService.SessionEstablished += OnNoiseSessionEstablished;
        _meshRoutingService.MessageDelivered += OnRoutedMessageDelivered;

        _gattServer.RoutedPacketReceived += OnRoutedPacketReceived;
        _gattServer.RouteDiscoveryReceived += p => Task.Run(() => _meshRoutingService.HandleRouteDiscovery(p));
        _gattServer.RouteReplyReceived += p => _meshRoutingService.HandleRouteReply(p);
        _gattServer.ChannelMessageReceived += p => Task.Run(() => _meshRoutingService.HandleChannelMessage(p));
        _gattServer.ChannelJoinReceived += OnChannelJoinPacketReceived;
        _gattServer.ChannelLeaveReceived += OnChannelLeavePacketReceived;
        _gattServer.ChannelAnnounceReceived += OnChannelAnnouncePacketReceived;

        // Wire channel service events
        _channelService.ChannelMessageReady += OnChannelMessageReady;
        _channelService.ChannelJoinBroadcast += ch => Task.Run(() => _meshRoutingService.SendChannelControlAsync(PacketType.ChannelJoin, ch));
        _channelService.ChannelLeaveBroadcast += ch => Task.Run(() => _meshRoutingService.SendChannelControlAsync(PacketType.ChannelLeave, ch));
        _channelService.ChannelAnnounceBroadcast += (ch, cnt) => Task.Run(() => _meshRoutingService.SendChannelControlAsync(PacketType.ChannelAnnounce, ch, cnt.ToString()));
        _channelService.ChannelMembersChanged += OnChannelMembersChanged;
        _channelService.DirectMessageRequested += OnDirectMessageRequested;

        _meshRoutingService.ChannelMessageDelivered += (ch, fp, text, trace) =>
            _dispatcher.Invoke(() => _channelService.OnChannelMessageReceived(ch, fp, text, trace));

        _channelService.ChannelMessageReceived += OnChannelMessageDisplayed;

        // Phase 3: lock check timer
        _lockCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _lockCheckTimer.Tick += (_, _) =>
        {
            if (_screenLockService.ShouldLock) ShowLockScreen();
        };
        _lockCheckTimer.Start();

        // Channel announce timer
        _channelAnnounceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _channelAnnounceTimer.Tick += (_, _) => _channelService.AnnounceAllChannels();
        _channelAnnounceTimer.Start();
    }

    // ============================================================
    // Initialization
    // ============================================================

    [RelayCommand]
    public async Task InitializeBleAsync()
    {
        try
        {
            _themeService.ApplyTheme(_settings.Current.Theme);
            _localizationService.ChangeLanguage("en-US");

            _identityService.LoadOrCreateIdentity(Username);
            _migrationService.MigrateIfNeeded();

            IdentityVm = new IdentityViewModel(_identityService, _verificationService, _trustService);
            OnPropertyChanged(nameof(IdentityVm));
            OnPropertyChanged(nameof(ShortFingerprint));

            // ---- Bluetooth capability check ----
            StatusText = "Detecting Bluetooth capabilities…";
            var (classic, ble) = BluetoothCapabilities.Detect();
            CapabilitiesText = BluetoothCapabilities.GetSummary();

            if (!classic && !ble)
            {
                IsBleAvailable = false;
                StatusText = "⚠ No Bluetooth adapter found";
                Log.Warning("No Bluetooth protocols available");
                return;
            }

            Log.Information("Bluetooth capabilities: Classic={Classic}, BLE={Ble}", classic, ble);

            var userId = _settings.Current.UserId;
            _messageService.SetIdentity(userId, Username);
            _fileTransferService.SetIdentity(userId);

            // Start Classic BT advertiser + listener
            if (classic)
                _advertiser.Start(userId, Username);

            // Start hybrid scanner (BLE + Classic)
            _hybridScanner.Start();
            await _gattServer.StartAsync();

            _channelService.JoinChannel("#general");

            IsBleAvailable = true;
            StatusText = $"Online — {ShortFingerprint} | {CapabilitiesText}";
            Log.Information("meshIt v3 initialized — {Fp} — {Caps}", ShortFingerprint, CapabilitiesText);
        }
        catch (Exception ex)
        {
            IsBleAvailable = false;
            StatusText = $"⚠ Bluetooth error: {ex.Message}";
            Log.Error(ex, "Bluetooth initialization failed");
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

        // Channel message
        if (CurrentChannelName is not null)
        {
            _channelService.SendChannelMessage(CurrentChannelName, MessageText.Trim());

            _dispatcher.Invoke(() => Messages.Add(new Message
            {
                SenderName = Username,
                Content = MessageText.Trim(),
                IsOutgoing = true,
                Timestamp = DateTime.Now,
                ChannelName = CurrentChannelName
            }));
            _messagesSentCount++;
            MessageText = string.Empty;
            return;
        }

        // Direct message
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

    [RelayCommand]
    private async Task SendDroppedFilesAsync(string[] filePaths)
    {
        if (SelectedPeer is null || filePaths is null) return;
        foreach (var file in filePaths)
            await _fileTransferService.SendFileAsync(SelectedPeer, file);
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
                _dispatcher.Invoke(() => Messages.Add(new Message
                {
                    SenderName = Username, Content = "🎤 Voice message",
                    IsOutgoing = true, Timestamp = DateTime.Now
                }));
                _messagesSentCount++;
            }
        }
        else
        {
            _audioService.StartRecording();
            IsRecordingVoice = true;
        }
    }

    [RelayCommand] private void ToggleSettings() { IsSettingsOpen = !IsSettingsOpen; IsIdentityOpen = false; IsDiagnosticsOpen = false; }
    [RelayCommand] private void ToggleIdentity() { IsIdentityOpen = !IsIdentityOpen; IsSettingsOpen = false; IsDiagnosticsOpen = false; }
    [RelayCommand]
    private void ToggleDiagnostics()
    {
        IsDiagnosticsOpen = !IsDiagnosticsOpen; IsSettingsOpen = false; IsIdentityOpen = false;
        if (IsDiagnosticsOpen) DiagnosticsVm.RefreshStats(Peers, _messagesSentCount, _messagesReceivedCount);
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
            "This will permanently delete ALL data.\n\nContinue?",
            "Emergency Wipe", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            _settings.ClearAllData();
            Messages.Clear();
            FileTransfers.Clear();
            StatusText = "🗑️ All data wiped";
        }
    }

    [RelayCommand]
    private void SelectChannel(string channelName)
    {
        CurrentChannelName = channelName;
        SelectedPeer = null;
        IsChannelMembersVisible = true;
        Messages.Clear();
        RefreshChannelMembers(channelName);
    }

    public void OnLogoTapped()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastLogoTap).TotalSeconds < 2) { _logoTapCount++; if (_logoTapCount >= 3) { _logoTapCount = 0; ClearDataCommand.Execute(null); } }
        else _logoTapCount = 1;
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
        CurrentChannelName = null;
        IsChannelMembersVisible = false;
        if (value is null) return;
        var history = _messageService.LoadMessages(value.Id);
        foreach (var m in history) Messages.Add(m);
    }

    // ============================================================
    // Bluetooth Event Handlers
    // ============================================================

    private void OnPeerDiscovered(DiscoveredPeer peer)
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
                _meshRoutingService.RegisterPeer(peer, null);
            }
        });
    }

    private void OnPeerLost(BluetoothAddress addr)
    {
        _dispatcher.Invoke(() =>
        {
            var peer = Peers.FirstOrDefault(p => p.BluetoothAddress == addr);
            if (peer is not null) peer.Status = PeerStatus.Offline;
        });
    }

    private void OnPeerDisconnected(BluetoothAddress addr)
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
            if (SelectedPeer is not null && msg.PeerId == SelectedPeer.Id) Messages.Add(msg);
            else _notificationService.ShowMessageNotification(msg.SenderName, msg.Content, msg.PeerId);
        });
    }

    private void OnTransferStarted(FileTransfer ft) => _dispatcher.Invoke(() => FileTransfers.Add(ft));
    private void OnTransferProgress(FileTransfer ft) => _dispatcher.Invoke(() =>
    {
        var e = FileTransfers.FirstOrDefault(x => x.FileId == ft.FileId);
        if (e is not null) { e.TransferredChunks = ft.TransferredChunks; e.Progress = ft.Progress; e.SpeedBytesPerSecond = ft.SpeedBytesPerSecond; }
    });
    private void OnTransferCompleted(FileTransfer ft) => _dispatcher.Invoke(() =>
    {
        var e = FileTransfers.FirstOrDefault(x => x.FileId == ft.FileId);
        if (e is not null) { e.Status = TransferStatus.Completed; e.Progress = 100; }
    });
    private void OnTransferFailed(FileTransfer ft, string error) => _dispatcher.Invoke(() =>
    {
        var e = FileTransfers.FirstOrDefault(x => x.FileId == ft.FileId);
        if (e is not null) e.Status = TransferStatus.Failed;
        Log.Warning("Transfer failed: {Error}", error);
    });

    // ============================================================
    // Mesh routing + channel handlers
    // ============================================================

    private void OnNoiseSessionEstablished(Guid peerId, NoiseSession session)
    {
        Log.Information("Noise session established with {Fp}", session.RemoteShortFingerprint);
        _dispatcher.Invoke(() => StatusText = $"🔒 Encrypted session with {session.RemoteShortFingerprint}");
    }

    private void OnRoutedMessageDelivered(RoutedMessage msg)
    {
        _messagesReceivedCount++;
        var trace = msg.RouteHistory.Count > 0
            ? string.Join(" → ", msg.RouteHistory.Select(f => f[..8])) : null;
        if (msg.ChannelName is not null) return;
        _dispatcher.Invoke(() => Messages.Add(new Message
        {
            SenderName = "Mesh", Content = Encoding.UTF8.GetString(msg.EncryptedPayload),
            IsOutgoing = false, Timestamp = DateTime.Now, RouteTrace = trace
        }));
    }

    private void OnRoutedPacketReceived(Packet packet)
    {
        try
        {
            var message = JsonSerializer.Deserialize<RoutedMessage>(packet.Payload);
            if (message is not null) _ = _meshRoutingService.RouteMessageAsync(message, Peers);
        }
        catch (Exception ex) { Log.Warning(ex, "Failed to deserialize routed message"); }
    }

    private void OnChannelJoinPacketReceived(Packet packet)
    {
        var senderFp = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(packet.OriginatorPublicKey)).ToLowerInvariant();
        var channelName = packet.ChannelName ?? "#general";
        var peerName = Encoding.UTF8.GetString(packet.Payload).Split('|')[0];
        _dispatcher.Invoke(() => _channelService.OnPeerJoinedChannel(channelName, senderFp, peerName));
        _ = _meshRoutingService.RoutePacketAsync(packet);
    }

    private void OnChannelLeavePacketReceived(Packet packet)
    {
        var senderFp = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(packet.OriginatorPublicKey)).ToLowerInvariant();
        var channelName = packet.ChannelName ?? "#general";
        var peerName = Encoding.UTF8.GetString(packet.Payload).Split('|')[0];
        _dispatcher.Invoke(() => _channelService.OnPeerLeftChannel(channelName, senderFp, peerName));
        _ = _meshRoutingService.RoutePacketAsync(packet);
    }

    private void OnChannelAnnouncePacketReceived(Packet packet)
    {
        var channelName = packet.ChannelName ?? "#general";
        var payloadStr = Encoding.UTF8.GetString(packet.Payload);
        var parts = payloadStr.Split('|');
        var memberCount = parts.Length > 1 && int.TryParse(parts[1], out var cnt) ? cnt : 1;
        _dispatcher.Invoke(() => _channelService.OnChannelAnnounce(channelName, memberCount));
        _ = _meshRoutingService.RoutePacketAsync(packet);
    }

    private void OnChannelMessageReady(string channelName, string text) =>
        _ = _meshRoutingService.SendChannelMessageAsync(channelName, text);

    private void OnChannelMessageDisplayed(string channelName, string senderFp, string text, string? routeTrace)
    {
        _messagesReceivedCount++;
        _dispatcher.Invoke(() =>
        {
            var senderName = senderFp[..8];
            var channel = _channelService.GetChannel(channelName);
            if (channel?.MemberNames.TryGetValue(senderFp, out var name) == true) senderName = name;

            var msg = new Message
            {
                SenderName = senderName, Content = text, IsOutgoing = false,
                Timestamp = DateTime.Now, ChannelName = channelName, RouteTrace = routeTrace
            };

            if (CurrentChannelName == channelName) Messages.Add(msg);
            else _notificationService.ShowMessageNotification(senderName, $"[{channelName}] {text}", Guid.Empty);
        });
    }

    private void OnChannelMembersChanged(string channelName)
    {
        if (CurrentChannelName == channelName)
            _dispatcher.Invoke(() => RefreshChannelMembers(channelName));
    }

    private void OnDirectMessageRequested(string recipientName, string messageText)
    {
        var peer = Peers.FirstOrDefault(p => p.Name.Equals(recipientName, StringComparison.OrdinalIgnoreCase));
        if (peer is not null) _ = _messageService.SendMessageAsync(peer, messageText);
        else _dispatcher.Invoke(() => Messages.Add(new Message
        {
            SenderName = "System", Content = $"❌ Peer '{recipientName}' not found",
            IsOutgoing = false, Timestamp = DateTime.Now
        }));
    }

    private void RefreshChannelMembers(string channelName)
    {
        CurrentChannelMembers.Clear();
        foreach (var name in _channelService.GetChannelMemberNames(channelName))
            CurrentChannelMembers.Add(name);
    }

    // ============================================================
    // Dispose
    // ============================================================

    public void Dispose()
    {
        _lockCheckTimer.Stop();
        _channelAnnounceTimer.Stop();
        _audioService.Dispose();
        _advertiser.Dispose();
        _hybridScanner.Dispose();
        _gattServer.Dispose();
        _connectionManager.Dispose();
        _meshRoutingService.Dispose();
    }
}
