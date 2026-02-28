using CommunityToolkit.Mvvm.ComponentModel;

namespace meshIt.Models;

/// <summary>
/// A single chat message exchanged between peers.
/// </summary>
public partial class Message : ObservableObject
{
    /// <summary>Database primary key / unique id.</summary>
    public int Id { get; set; }

    /// <summary>GUID of the peer who sent the message.</summary>
    public Guid SenderId { get; set; }

    /// <summary>Display name of the sender at the time the message was sent.</summary>
    public string SenderName { get; set; } = string.Empty;

    /// <summary>Plain-text content of the message.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the message was created.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>True if this message was sent by the local user.</summary>
    public bool IsOutgoing { get; set; }

    /// <summary>GUID of the peer this conversation belongs to (the other party).</summary>
    public Guid PeerId { get; set; }

    /// <summary>Channel name if this is a channel message (null = DM).</summary>
    public string? ChannelName { get; set; }

    /// <summary>Route trace string showing the path taken (e.g. "You → Alice → Bob").</summary>
    public string? RouteTrace { get; set; }
}
