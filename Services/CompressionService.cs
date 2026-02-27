using K4os.Compression.LZ4;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// LZ4 compression for messages larger than 100 bytes.
/// Only compresses if the result is actually smaller.
/// </summary>
public static class CompressionService
{
    private const int CompressionThreshold = 100;

    /// <summary>
    /// Compress data using LZ4. Returns (compressedData, wasCompressed).
    /// If the input is too small or compression doesn't save space, returns the original bytes.
    /// </summary>
    public static (byte[] data, bool wasCompressed) Compress(byte[] data)
    {
        if (data.Length < CompressionThreshold)
            return (data, false);

        try
        {
            var maxOutput = LZ4Codec.MaximumOutputSize(data.Length);
            var buffer = new byte[maxOutput];
            var compressedSize = LZ4Codec.Encode(data, buffer, LZ4Level.L00_FAST);

            if (compressedSize > 0 && compressedSize < data.Length)
            {
                var result = new byte[compressedSize];
                Array.Copy(buffer, result, compressedSize);
                Log.Debug("Compressed {Original} → {Compressed} bytes ({Ratio:P0})",
                    data.Length, compressedSize, (double)compressedSize / data.Length);
                return (result, true);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "LZ4 compression failed, using uncompressed data");
        }

        return (data, false);
    }

    /// <summary>
    /// Decompress an LZ4-compressed buffer. <paramref name="originalSize"/> is needed
    /// for the target buffer; if unknown, a generous estimate is used.
    /// </summary>
    public static byte[] Decompress(byte[] data, int originalSize = 0)
    {
        try
        {
            if (originalSize <= 0) originalSize = data.Length * 4; // generous estimate
            var buffer = new byte[originalSize];
            var decodedSize = LZ4Codec.Decode(data, buffer);

            if (decodedSize > 0)
            {
                var result = new byte[decodedSize];
                Array.Copy(buffer, result, decodedSize);
                return result;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "LZ4 decompression failed");
        }

        // If decompression fails, return original data
        return data;
    }
}
