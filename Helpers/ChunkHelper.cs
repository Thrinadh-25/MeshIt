using System.IO;
using meshIt.Models;

namespace meshIt.Helpers;

/// <summary>
/// Splits files into MTU-sized chunks for BLE transmission and reassembles them on the
/// receiving side.
/// </summary>
public static class ChunkHelper
{
    /// <summary>
    /// Usable data bytes per chunk (MTU minus packet header minus 16 bytes for fileId
    /// prepended in <see cref="PacketBuilder.CreateFileChunk"/>).
    /// </summary>
    private static readonly int ChunkDataSize = BleConstants.MaxPayloadSize - 16;

    /// <summary>
    /// Calculate how many chunks a file of the given size will produce.
    /// </summary>
    public static int GetTotalChunks(long fileSize)
    {
        if (ChunkDataSize <= 0) return 1;
        return (int)Math.Ceiling((double)fileSize / ChunkDataSize);
    }

    /// <summary>
    /// Read a specific chunk (0-based index) from the file at <paramref name="filePath"/>.
    /// </summary>
    public static async Task<byte[]> ReadChunkAsync(string filePath, int chunkIndex)
    {
        var fileInfo = new FileInfo(filePath);
        var offset = (long)chunkIndex * ChunkDataSize;
        var remaining = fileInfo.Length - offset;
        var size = (int)Math.Min(remaining, ChunkDataSize);

        var buffer = new byte[size];
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        fs.Seek(offset, SeekOrigin.Begin);
        _ = await fs.ReadAsync(buffer.AsMemory(0, size));
        return buffer;
    }

    /// <summary>
    /// Write a received chunk to the correct offset in the destination file.
    /// The file is created if it does not exist.
    /// </summary>
    public static async Task WriteChunkAsync(string destinationPath, int chunkIndex, byte[] chunkData, long totalFileSize)
    {
        var offset = (long)chunkIndex * ChunkDataSize;

        // Ensure the file exists with the correct size
        if (!File.Exists(destinationPath))
        {
            await using var create = new FileStream(destinationPath, FileMode.Create, FileAccess.Write);
            create.SetLength(totalFileSize);
        }

        await using var fs = new FileStream(destinationPath, FileMode.Open, FileAccess.Write, FileShare.None);
        fs.Seek(offset, SeekOrigin.Begin);
        await fs.WriteAsync(chunkData);
    }
}
