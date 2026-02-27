using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using meshIt.Models;

namespace meshIt.ViewModels;

/// <summary>
/// View model for the peer list sidebar. Wraps the shared peer
/// collection and tracks the currently selected peer.
/// </summary>
public partial class PeerListViewModel : ObservableObject
{
    /// <summary>Observable peer list (bound from MainViewModel).</summary>
    public ObservableCollection<Peer> Peers { get; }

    [ObservableProperty] private Peer? _selectedPeer;

    public PeerListViewModel(ObservableCollection<Peer> peers)
    {
        Peers = peers;
    }
}
