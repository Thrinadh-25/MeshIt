using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using meshIt.Models;

namespace meshIt.ViewModels;

/// <summary>
/// ViewModel for the network diagnostics dashboard.
/// </summary>
public partial class DiagnosticsViewModel : ObservableObject
{
    // ---- Connection stats ----
    [ObservableProperty] private int _activePeers;
    [ObservableProperty] private int _totalPeersDiscovered;
    [ObservableProperty] private long _messagesSent;
    [ObservableProperty] private long _messagesReceived;
    [ObservableProperty] private long _bytesSent;
    [ObservableProperty] private long _bytesReceived;
    [ObservableProperty] private int _meshHopsRelayed;
    [ObservableProperty] private int _pendingStoreForward;
    [ObservableProperty] private int _noiseSessionsActive;
    [ObservableProperty] private int _channelsJoined;

    // ---- Signal history (last 50 readings for a chart-like display) ----
    public ObservableCollection<SignalReading> SignalHistory { get; } = new();

    // ---- Crash logs ----
    [ObservableProperty] private int _crashLogCount;

    /// <summary>Update stats from current service state.</summary>
    public void RefreshStats(IEnumerable<Peer> peers, long sent, long received)
    {
        var peerList = peers.ToList();
        ActivePeers = peerList.Count(p => p.Status == PeerStatus.Online);
        TotalPeersDiscovered = peerList.Count;
        MessagesSent = sent;
        MessagesReceived = received;
    }

    /// <summary>Record a signal reading.</summary>
    public void AddSignalReading(string peerName, int rssi)
    {
        SignalHistory.Add(new SignalReading
        {
            PeerName = peerName,
            Rssi = rssi,
            Timestamp = DateTime.Now
        });

        // Keep last 50
        while (SignalHistory.Count > 50)
            SignalHistory.RemoveAt(0);
    }

    [RelayCommand]
    private void ClearHistory()
    {
        SignalHistory.Clear();
    }
}

/// <summary>Represents a single RSSI reading for the diagnostics chart.</summary>
public class SignalReading
{
    public string PeerName { get; set; } = "";
    public int Rssi { get; set; }
    public DateTime Timestamp { get; set; }
}
