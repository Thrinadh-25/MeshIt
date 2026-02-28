using System.Collections.Concurrent;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// Splits large messages into BLE-sized chunks and reassembles them.
/// Each chunk has a header: [MessageId(4)][ChunkIndex(2)][TotalChunks(2)][data].
/// Default chunk size is 200 bytes (BLE MTU minus overhead).
/// </summary>
public class MessageChunker
{
    private const int HeaderSize = 8; // 4 + 2 + 2
    private const int DefaultMaxChunkData = 200;

    private static readonly ConcurrentDictionary<int, byte[]?[]> _reassemblyBuffers = new();
    private static readonly ConcurrentDictionary<int, int> _receivedCounts = new();

    /// <summary>
    /// Split data into chunks suitable for BLE transmission.
    /// </summary>
    public static List<byte[]> Split(byte[] data, int maxDataSize = DefaultMaxChunkData)
    {
        var chunks = new List<byte[]>();
        var messageId = data.GetHashCode();
        var totalChunks = (int)Math.Ceiling(data.Length / (double)maxDataSize);

        if (totalChunks > ushort.MaxValue)
        {
            Log.Warning("MessageChunker: Data too large to chunk ({Length} bytes)", data.Length);
            totalChunks = ushort.MaxValue;
        }

        for (var i = 0; i < totalChunks; i++)
        {
            var offset = i * maxDataSize;
            var chunkSize = Math.Min(maxDataSize, data.Length - offset);
            var chunk = new byte[HeaderSize + chunkSize];

            // Header
            BitConverter.GetBytes(messageId).CopyTo(chunk, 0);
            BitConverter.GetBytes((ushort)i).CopyTo(chunk, 4);
            BitConverter.GetBytes((ushort)totalChunks).CopyTo(chunk, 6);

            // Data
            Buffer.BlockCopy(data, offset, chunk, HeaderSize, chunkSize);
            chunks.Add(chunk);
        }

        Log.Debug("MessageChunker: Split {Length} bytes into {Count} chunks", data.Length, chunks.Count);
        return chunks;
    }

    /// <summary>
    /// Reassemble a received chunk. Returns the complete message when all chunks are received,
    /// or null if more chunks are needed.
    /// </summary>
    public static byte[]? Reassemble(byte[] chunk)
    {
        if (chunk.Length < HeaderSize) return null;

        var messageId = BitConverter.ToInt32(chunk, 0);
        var chunkIndex = BitConverter.ToUInt16(chunk, 4);
        var totalChunks = BitConverter.ToUInt16(chunk, 6);

        if (chunkIndex >= totalChunks) return null;

        // Get or create buffer
        var buffer = _reassemblyBuffers.GetOrAdd(messageId, _ => new byte[totalChunks][]);
        _receivedCounts.TryAdd(messageId, 0);

        // Extract data portion
        var data = new byte[chunk.Length - HeaderSize];
        Buffer.BlockCopy(chunk, HeaderSize, data, 0, data.Length);

        // Store chunk
        if (buffer[chunkIndex] == null)
        {
            buffer[chunkIndex] = data;
            _receivedCounts.AddOrUpdate(messageId, 1, (_, count) => count + 1);
        }

        // Check if complete
        if (_receivedCounts.TryGetValue(messageId, out var received) && received >= totalChunks)
        {
            // Reassemble
            var totalSize = buffer.Where(b => b != null).Sum(b => b!.Length);
            var result = new byte[totalSize];
            var offset = 0;

            for (var i = 0; i < totalChunks; i++)
            {
                if (buffer[i] != null)
                {
                    Buffer.BlockCopy(buffer[i]!, 0, result, offset, buffer[i]!.Length);
                    offset += buffer[i]!.Length;
                }
            }

            // Cleanup
            _reassemblyBuffers.TryRemove(messageId, out _);
            _receivedCounts.TryRemove(messageId, out _);

            Log.Debug("MessageChunker: Reassembled {Length} bytes from {Count} chunks",
                result.Length, totalChunks);
            return result;
        }

        return null; // More chunks needed
    }

    /// <summary>
    /// Clean up stale buffers that haven't completed assembly (call periodically).
    /// </summary>
    public static void CleanupStaleBuffers()
    {
        // Simple cleanup — remove all incomplete buffers
        // In production, track timestamps and expire after timeout
        if (_reassemblyBuffers.Count > 100)
        {
            _reassemblyBuffers.Clear();
            _receivedCounts.Clear();
            Log.Debug("MessageChunker: Cleaned up stale reassembly buffers");
        }
    }
}
