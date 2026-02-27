using CommunityToolkit.Mvvm.ComponentModel;

namespace meshIt.Models;

/// <summary>
/// Represents an IRC-style channel for group messaging.
/// </summary>
public partial class Channel : ObservableObject
{
    /// <summary>Channel name (e.g. "#general"). Always lowercase, starts with #.</summary>
    [ObservableProperty] private string _name = string.Empty;

    /// <summary>Optional password for protected channels.</summary>
    public string? Password { get; set; }

    /// <summary>Public keys of current members.</summary>
    public HashSet<string> MemberFingerprints { get; set; } = new();

    /// <summary>Number of members.</summary>
    [ObservableProperty] private int _memberCount;

    /// <summary>When the channel was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Whether the local user has joined this channel.</summary>
    [ObservableProperty] private bool _isJoined;
}
