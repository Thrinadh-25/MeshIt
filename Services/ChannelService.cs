using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using meshIt.Models;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// IRC-style channel system for group messaging.
/// Supports /join, /leave, /msg, /who commands.
/// </summary>
public class ChannelService
{
    private readonly ConcurrentDictionary<string, Channel> _channels = new();
    private readonly IdentityService _identityService;

    /// <summary>Observable collection of channels the local user has joined.</summary>
    public ObservableCollection<Channel> JoinedChannels { get; } = new();

    /// <summary>Fired when a channel message should be sent to members.</summary>
    public event Action<string, string, List<string>>? ChannelMessageReady;
    // channelName, messageText, list of member fingerprints

    /// <summary>Fired when a channel message is received.</summary>
    public event Action<string, string, string>? ChannelMessageReceived;
    // channelName, senderFingerprint, messageText

    public ChannelService(IdentityService identityService)
    {
        _identityService = identityService;
    }

    /// <summary>
    /// Join a channel. Creates it if it doesn't exist.
    /// </summary>
    public Channel JoinChannel(string channelName, string? password = null)
    {
        channelName = NormalizeName(channelName);
        var identity = _identityService.CurrentIdentity!;

        var channel = _channels.GetOrAdd(channelName, _ => new Channel
        {
            Name = channelName,
            Password = password,
            CreatedAt = DateTime.UtcNow
        });

        // Verify password
        if (channel.Password is not null && channel.Password != password)
            throw new UnauthorizedAccessException($"Incorrect password for {channelName}");

        if (channel.MemberFingerprints.Add(identity.Fingerprint))
        {
            channel.MemberCount = channel.MemberFingerprints.Count;
            channel.IsJoined = true;

            if (!JoinedChannels.Any(c => c.Name == channelName))
                JoinedChannels.Add(channel);

            Log.Information("Joined channel {Channel} ({Members} members)",
                channelName, channel.MemberCount);
        }

        return channel;
    }

    /// <summary>Leave a channel.</summary>
    public void LeaveChannel(string channelName)
    {
        channelName = NormalizeName(channelName);
        var identity = _identityService.CurrentIdentity!;

        if (_channels.TryGetValue(channelName, out var channel))
        {
            channel.MemberFingerprints.Remove(identity.Fingerprint);
            channel.MemberCount = channel.MemberFingerprints.Count;
            channel.IsJoined = false;

            var joined = JoinedChannels.FirstOrDefault(c => c.Name == channelName);
            if (joined is not null) JoinedChannels.Remove(joined);

            Log.Information("Left channel {Channel}", channelName);
        }
    }

    /// <summary>Send a message to all members of a channel.</summary>
    public void SendChannelMessage(string channelName, string text)
    {
        channelName = NormalizeName(channelName);
        var identity = _identityService.CurrentIdentity!;

        if (!_channels.TryGetValue(channelName, out var channel) || !channel.IsJoined)
        {
            Log.Warning("Not a member of {Channel}", channelName);
            return;
        }

        var otherMembers = channel.MemberFingerprints
            .Where(fp => fp != identity.Fingerprint)
            .ToList();

        if (otherMembers.Count > 0)
            ChannelMessageReady?.Invoke(channelName, text, otherMembers);

        Log.Debug("Sent channel message to {Channel} ({Count} recipients)", channelName, otherMembers.Count);
    }

    /// <summary>Handle an incoming channel message.</summary>
    public void OnChannelMessageReceived(string channelName, string senderFingerprint, string text)
    {
        channelName = NormalizeName(channelName);

        // Auto-join if we receive a message for a channel we're not in
        if (!_channels.ContainsKey(channelName))
            JoinChannel(channelName);

        // Add sender as member
        if (_channels.TryGetValue(channelName, out var channel))
        {
            channel.MemberFingerprints.Add(senderFingerprint);
            channel.MemberCount = channel.MemberFingerprints.Count;
        }

        ChannelMessageReceived?.Invoke(channelName, senderFingerprint, text);
    }

    /// <summary>
    /// Process an IRC-style command from the user input.
    /// Returns (handled, response) — where response is a system message to show.
    /// </summary>
    public (bool handled, string? response) ProcessCommand(string input)
    {
        if (!input.StartsWith("/")) return (false, null);

        var parts = input.Split(' ', 2, StringSplitOptions.TrimEntries);
        var cmd = parts[0].ToLower();
        var arg = parts.Length > 1 ? parts[1] : string.Empty;

        return cmd switch
        {
            "/join" when !string.IsNullOrEmpty(arg) =>
                (true, JoinCommandResult(arg)),
            "/leave" when !string.IsNullOrEmpty(arg) =>
                (true, LeaveCommandResult(arg)),
            "/who" => (true, WhoCommandResult(arg)),
            "/channels" => (true, ListChannelsResult()),
            _ => (false, null)
        };
    }

    private string JoinCommandResult(string arg)
    {
        try { JoinChannel(arg); return $"Joined {NormalizeName(arg)}"; }
        catch (Exception ex) { return $"Failed to join: {ex.Message}"; }
    }

    private string LeaveCommandResult(string arg)
    {
        LeaveChannel(arg);
        return $"Left {NormalizeName(arg)}";
    }

    private string WhoCommandResult(string channelName)
    {
        channelName = NormalizeName(channelName);
        if (!_channels.TryGetValue(channelName, out var ch))
            return $"{channelName} — not found";
        return $"{channelName} — {ch.MemberCount} member(s): " +
               string.Join(", ", ch.MemberFingerprints.Select(f => f[..8]));
    }

    private string ListChannelsResult() =>
        JoinedChannels.Count == 0
            ? "No active channels"
            : string.Join(", ", JoinedChannels.Select(c => c.Name));

    private static string NormalizeName(string name)
    {
        name = name.Trim().ToLower();
        if (!name.StartsWith('#')) name = "#" + name;
        return name;
    }
}
