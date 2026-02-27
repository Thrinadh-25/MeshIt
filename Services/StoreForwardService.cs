using System.IO;
using System.Text.Json;
using meshIt.Models;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// Caches messages destined for offline peers and delivers them when the peer reconnects.
/// Messages are stored as JSON-lines files per-peer, capped at 100 messages / 7-day expiry.
/// </summary>
public class StoreForwardService
{
    private static readonly string QueueDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "meshIt", "pending");

    private const int MaxMessagesPerPeer = 100;

    public StoreForwardService()
    {
        Directory.CreateDirectory(QueueDir);
    }

    /// <summary>Queue a message for an offline peer.</summary>
    public void QueueMessage(string destinationFingerprint, byte[] encryptedPayload)
    {
        var queueFile = Path.Combine(QueueDir, $"{destinationFingerprint}.jsonl");

        var pending = new PendingMessage
        {
            DestinationFingerprint = destinationFingerprint,
            EncryptedPayloadBase64 = Convert.ToBase64String(encryptedPayload)
        };

        // Enforce cap
        if (File.Exists(queueFile))
        {
            var lines = File.ReadAllLines(queueFile);
            if (lines.Length >= MaxMessagesPerPeer)
            {
                // Remove oldest messages
                var trimmed = lines.Skip(lines.Length - MaxMessagesPerPeer + 1).ToArray();
                File.WriteAllLines(queueFile, trimmed);
            }
        }

        File.AppendAllText(queueFile,
            JsonSerializer.Serialize(pending) + Environment.NewLine);

        Log.Information("Store-forward: Queued message for offline peer {Fp}", destinationFingerprint[..8]);
    }

    /// <summary>
    /// Flush all queued messages for a peer that just came online.
    /// Returns the list of encrypted payloads to deliver.
    /// </summary>
    public List<byte[]> FlushQueue(string destinationFingerprint)
    {
        var queueFile = Path.Combine(QueueDir, $"{destinationFingerprint}.jsonl");
        var delivered = new List<byte[]>();

        if (!File.Exists(queueFile)) return delivered;

        try
        {
            var lines = File.ReadAllLines(queueFile);
            var remaining = new List<string>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var pending = JsonSerializer.Deserialize<PendingMessage>(line);
                if (pending is null) continue;

                if (pending.IsExpired)
                {
                    Log.Debug("Store-forward: Dropping expired message {Id}", pending.MessageId);
                    continue;
                }

                delivered.Add(Convert.FromBase64String(pending.EncryptedPayloadBase64));
            }

            // Clear the queue file after flushing
            File.Delete(queueFile);
            Log.Information("Store-forward: Flushed {Count} messages for {Fp}",
                delivered.Count, destinationFingerprint[..8]);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Store-forward: Failed to flush queue for {Fp}", destinationFingerprint);
        }

        return delivered;
    }

    /// <summary>Get the count of pending messages across all peers.</summary>
    public int GetTotalPendingCount()
    {
        if (!Directory.Exists(QueueDir)) return 0;
        return Directory.GetFiles(QueueDir, "*.jsonl")
            .Sum(f => File.ReadAllLines(f).Count(l => !string.IsNullOrWhiteSpace(l)));
    }
}
