using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using meshIt.Models;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// IRC-style channel system for group messaging.
/// Supports /join, /leave, /msg, /who, /channels, /help commands.
/// Includes channel announce/discovery and member tracking with names.
/// </summary>
public class ChannelService
{
    private readonly ConcurrentDictionary<string, Channel> _channels = new();
    private readonly IdentityService _identityService;

    /// <summary>Observable collection of channels the local user has joined.</summary>
    public ObservableCollection<Channel> JoinedChannels { get; } = new();

    /// <summary>Observable collection of discovered (available but not joined) channels.</summary>
    public ObservableCollection<Channel> AvailableChannels { get; } = new();

    // ---- Events for outgoing network operations ----

    /// <summary>Fired when a channel message should be sent via mesh routing.</summary>
    public event Action<string, string>? ChannelMessageReady;
    // channelName, messageText

    /// <summary>Fired when a channel join should be broadcast.</summary>
    public event Action<string>? ChannelJoinBroadcast;
    // channelName

    /// <summary>Fired when a channel leave should be broadcast.</summary>
    public event Action<string>? ChannelLeaveBroadcast;
    // channelName

    /// <summary>Fired when a channel announcement should be broadcast.</summary>
    public event Action<string, int>? ChannelAnnounceBroadcast;
    // channelName, memberCount

    /// <summary>Fired when a channel message is received for local display.</summary>
    public event Action<string, string, string, string?>? ChannelMessageReceived;
    // channelName, senderFingerprint, messageText, routeTrace

    /// <summary>Fired when channel members change (for UI refresh).</summary>
    public event Action<string>? ChannelMembersChanged;
    // channelName

    /// <summary>Fired when a /msg direct message is requested.</summary>
    public event Action<string, string>? DirectMessageRequested;
    // recipientName, messageText

    public ChannelService(IdentityService identityService)
    {
        _identityService = identityService;
    }

    // ============================================================
    // Channel Operations
    // ============================================================

    /// <summary>Join a channel. Creates it if it doesn't exist.</summary>
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
            channel.MemberNames[identity.Fingerprint] = identity.Nickname;
            channel.MemberCount = channel.MemberFingerprints.Count;
            channel.IsJoined = true;
            channel.LastActivity = DateTime.UtcNow;

            if (!JoinedChannels.Any(c => c.Name == channelName))
                JoinedChannels.Add(channel);

            // Remove from available list
            var avail = AvailableChannels.FirstOrDefault(c => c.Name == channelName);
            if (avail is not null) AvailableChannels.Remove(avail);

            Log.Information("Joined channel {Channel} ({Members} members)",
                channelName, channel.MemberCount);

            // Broadcast join to network
            ChannelJoinBroadcast?.Invoke(channelName);
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
            channel.MemberNames.Remove(identity.Fingerprint);
            channel.MemberCount = channel.MemberFingerprints.Count;
            channel.IsJoined = false;

            var joined = JoinedChannels.FirstOrDefault(c => c.Name == channelName);
            if (joined is not null) JoinedChannels.Remove(joined);

            Log.Information("Left channel {Channel}", channelName);

            // Broadcast leave to network
            ChannelLeaveBroadcast?.Invoke(channelName);
        }
    }

    /// <summary>Send a message to a channel.</summary>
    public void SendChannelMessage(string channelName, string text)
    {
        channelName = NormalizeName(channelName);

        if (!_channels.TryGetValue(channelName, out var channel) || !channel.IsJoined)
        {
            Log.Warning("Not a member of {Channel}", channelName);
            return;
        }

        channel.LastActivity = DateTime.UtcNow;
        ChannelMessageReady?.Invoke(channelName, text);

        Log.Debug("Sent channel message to {Channel}", channelName);
    }

    // ============================================================
    // Incoming Network Handlers
    // ============================================================

    /// <summary>Handle incoming channel message from mesh.</summary>
    public void OnChannelMessageReceived(string channelName, string senderFingerprint,
        string text, string? routeTrace = null)
    {
        channelName = NormalizeName(channelName);

        // Track the channel even if we haven't joined
        if (!_channels.ContainsKey(channelName))
        {
            var ch = new Channel { Name = channelName };
            _channels.TryAdd(channelName, ch);
        }

        if (_channels.TryGetValue(channelName, out var channel))
        {
            channel.MemberFingerprints.Add(senderFingerprint);
            channel.MemberCount = channel.MemberFingerprints.Count;
            channel.LastActivity = DateTime.UtcNow;
        }

        // Only deliver to UI if we've joined this channel
        if (channel?.IsJoined == true)
        {
            ChannelMessageReceived?.Invoke(channelName, senderFingerprint, text, routeTrace);
        }
    }

    /// <summary>Handle incoming channel join broadcast.</summary>
    public void OnPeerJoinedChannel(string channelName, string peerFingerprint, string peerName)
    {
        channelName = NormalizeName(channelName);

        var channel = _channels.GetOrAdd(channelName, _ => new Channel { Name = channelName });
        channel.MemberFingerprints.Add(peerFingerprint);
        channel.MemberNames[peerFingerprint] = peerName;
        channel.MemberCount = channel.MemberFingerprints.Count;
        channel.LastActivity = DateTime.UtcNow;

        // Add to available channels if not already joined
        if (!channel.IsJoined && !AvailableChannels.Any(c => c.Name == channelName))
            AvailableChannels.Add(channel);

        ChannelMembersChanged?.Invoke(channelName);
        Log.Information("Peer {Name} joined {Channel}", peerName, channelName);
    }

    /// <summary>Handle incoming channel leave broadcast.</summary>
    public void OnPeerLeftChannel(string channelName, string peerFingerprint, string peerName)
    {
        channelName = NormalizeName(channelName);

        if (_channels.TryGetValue(channelName, out var channel))
        {
            channel.MemberFingerprints.Remove(peerFingerprint);
            channel.MemberNames.Remove(peerFingerprint);
            channel.MemberCount = channel.MemberFingerprints.Count;
        }

        ChannelMembersChanged?.Invoke(channelName);
        Log.Information("Peer {Name} left {Channel}", peerName, channelName);
    }

    /// <summary>Handle incoming channel announcement (discovery).</summary>
    public void OnChannelAnnounce(string channelName, int memberCount)
    {
        channelName = NormalizeName(channelName);

        var channel = _channels.GetOrAdd(channelName, _ => new Channel { Name = channelName });
        channel.MemberCount = memberCount;
        channel.LastActivity = DateTime.UtcNow;

        if (!channel.IsJoined && !AvailableChannels.Any(c => c.Name == channelName))
            AvailableChannels.Add(channel);

        Log.Debug("Channel announce: {Channel} ({Members} members)", channelName, memberCount);
    }

    /// <summary>Broadcast announcements for all joined channels.</summary>
    public void AnnounceAllChannels()
    {
        foreach (var channel in JoinedChannels)
        {
            ChannelAnnounceBroadcast?.Invoke(channel.Name, channel.MemberCount);
        }
    }

    // ============================================================
    // IRC Command Processing
    // ============================================================

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
            "/channels" or "/list" => (true, ListChannelsResult()),
            "/msg" when !string.IsNullOrEmpty(arg) => (true, MsgCommandResult(arg)),
            "/help" => (true, HelpCommandResult()),
            _ => (false, null)
        };
    }

    private string JoinCommandResult(string arg)
    {
        try { JoinChannel(arg); return $"✅ Joined {NormalizeName(arg)}"; }
        catch (Exception ex) { return $"❌ Failed to join: {ex.Message}"; }
    }

    private string LeaveCommandResult(string arg)
    {
        LeaveChannel(arg);
        return $"👋 Left {NormalizeName(arg)}";
    }

    private string WhoCommandResult(string channelName)
    {
        if (string.IsNullOrEmpty(channelName))
        {
            // Show members of all joined channels
            if (JoinedChannels.Count == 0)
                return "Not in any channels. Use /join #channel";

            return string.Join("\n", JoinedChannels.Select(c =>
                $"{c.Name} — {c.MemberCount} member(s): " +
                string.Join(", ", c.MemberNames.Values)));
        }

        channelName = NormalizeName(channelName);
        if (!_channels.TryGetValue(channelName, out var ch))
            return $"❌ {channelName} — not found";

        var members = ch.MemberNames.Count > 0
            ? string.Join(", ", ch.MemberNames.Values)
            : string.Join(", ", ch.MemberFingerprints.Select(f => f[..8]));

        return $"👥 {channelName} — {ch.MemberCount} member(s): {members}";
    }

    private string ListChannelsResult()
    {
        var joined = JoinedChannels.Count > 0
            ? "📌 Joined: " + string.Join(", ", JoinedChannels.Select(c => c.Name))
            : "No joined channels";

        var available = AvailableChannels.Count > 0
            ? "\n📡 Available: " + string.Join(", ", AvailableChannels.Select(c => $"{c.Name} ({c.MemberCount})"))
            : "";

        return joined + available;
    }

    private string MsgCommandResult(string arg)
    {
        var parts = arg.Split(' ', 2, StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return "Usage: /msg <username> <message>";

        var recipient = parts[0];
        var message = parts[1];
        DirectMessageRequested?.Invoke(recipient, message);
        return $"📨 DM to {recipient}: {message}";
    }

    private static string HelpCommandResult() =>
        """
        📖 Available Commands:
        /join #channel     — Join a channel
        /leave #channel    — Leave a channel
        /channels          — List all channels
        /who [#channel]    — Show channel members
        /msg <user> <text> — Send direct message
        /help              — Show this help
        """;

    // ============================================================
    // Helpers
    // ============================================================

    /// <summary>Get channel members by name for a given channel.</summary>
    public List<string> GetChannelMemberNames(string channelName)
    {
        channelName = NormalizeName(channelName);
        if (_channels.TryGetValue(channelName, out var channel))
            return channel.MemberNames.Values.ToList();
        return new List<string>();
    }

    /// <summary>Get a channel by name.</summary>
    public Channel? GetChannel(string channelName)
    {
        channelName = NormalizeName(channelName);
        return _channels.TryGetValue(channelName, out var ch) ? ch : null;
    }

    /// <summary>Look up a peer fingerprint by nickname across all channels.</summary>
    public string? FindFingerprintByName(string name)
    {
        foreach (var channel in _channels.Values)
        {
            var match = channel.MemberNames.FirstOrDefault(kv =>
                kv.Value.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match.Key != null) return match.Key;
        }
        return null;
    }

    private static string NormalizeName(string name)
    {
        name = name.Trim().ToLower();
        if (!name.StartsWith('#')) name = "#" + name;
        return name;
    }
}
