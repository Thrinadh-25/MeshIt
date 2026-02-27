using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using meshIt.Helpers;
using meshIt.Models;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// Manages chunked file sending and receiving over BLE.
/// Implements ACK-based flow control (ACK every <see cref="BleConstants.AckWindowSize"/> chunks).
/// </summary>
public sealed class FileTransferService
{
    private readonly BleConnectionManager _connectionManager;
    private readonly GattServerService _gattServer;
    private Guid _localUserId;
    private uint _seqCounter;

    /// <summary>Active incoming transfers keyed by fileId.</summary>
    private readonly ConcurrentDictionary<Guid, IncomingTransfer> _incoming = new();

    /// <summary>Fired when a new incoming transfer starts.</summary>
    public event Action<FileTransfer>? TransferStarted;

    /// <summary>Fired when transfer progress changes.</summary>
    public event Action<FileTransfer>? TransferProgress;

    /// <summary>Fired when a transfer completes.</summary>
    public event Action<FileTransfer>? TransferCompleted;

    /// <summary>Fired when a transfer fails.</summary>
    public event Action<FileTransfer, string>? TransferFailed;

    public FileTransferService(BleConnectionManager connectionManager, GattServerService gattServer)
    {
        _connectionManager = connectionManager;
        _gattServer = gattServer;

        _gattServer.FileDataReceived += OnFileDataReceived;
    }

    public void SetIdentity(Guid userId) => _localUserId = userId;

    /// <summary>
    /// Send a file to the specified peer.
    /// </summary>
    public async Task<FileTransfer> SendFileAsync(Peer peer, string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("File not found", filePath);

        var transfer = new FileTransfer
        {
            FileId = Guid.NewGuid(),
            FileName = fileInfo.Name,
            FileSize = fileInfo.Length,
            TotalChunks = ChunkHelper.GetTotalChunks(fileInfo.Length),
            IsOutgoing = true,
            PeerId = peer.Id,
            Status = TransferStatus.InProgress,
            LocalPath = filePath
        };

        TransferStarted?.Invoke(transfer);

        try
        {
            // Ensure connection
            if (!await _connectionManager.ConnectAsync(peer.BluetoothAddress))
            {
                transfer.Status = TransferStatus.Failed;
                TransferFailed?.Invoke(transfer, "Could not connect to peer");
                return transfer;
            }

            // 1. Send file metadata
            var metaSeq = Interlocked.Increment(ref _seqCounter);
            var metaPacket = PacketBuilder.CreateFileMetadata(
                _localUserId, transfer.FileId, transfer.FileName,
                transfer.FileSize, transfer.TotalChunks, metaSeq);
            metaPacket.Payload = EncryptionService.Encrypt(metaPacket.Payload);
            var metaBytes = PacketBuilder.Serialize(metaPacket);
            await _connectionManager.SendFileDataAsync(peer.BluetoothAddress, metaBytes);

            Log.Information("Sending file '{FileName}' ({Size} bytes, {Chunks} chunks) to {Peer}",
                transfer.FileName, transfer.FileSize, transfer.TotalChunks, peer.Name);

            var sw = Stopwatch.StartNew();

            // 2. Send chunks
            for (var i = 0; i < transfer.TotalChunks; i++)
            {
                var chunkData = await ChunkHelper.ReadChunkAsync(filePath, i);
                var chunkSeq = Interlocked.Increment(ref _seqCounter);
                var chunkPacket = PacketBuilder.CreateFileChunk(_localUserId, transfer.FileId, chunkData, chunkSeq);
                chunkPacket.Payload = EncryptionService.Encrypt(chunkPacket.Payload);
                var chunkBytes = PacketBuilder.Serialize(chunkPacket);

                var sent = await _connectionManager.SendFileDataAsync(peer.BluetoothAddress, chunkBytes);
                if (!sent)
                {
                    transfer.Status = TransferStatus.Failed;
                    TransferFailed?.Invoke(transfer, $"Failed to send chunk {i}");
                    return transfer;
                }

                transfer.TransferredChunks = i + 1;
                transfer.Progress = (double)(i + 1) / transfer.TotalChunks * 100;
                transfer.SpeedBytesPerSecond = chunkData.Length / (sw.Elapsed.TotalSeconds == 0 ? 1 : sw.Elapsed.TotalSeconds);
                TransferProgress?.Invoke(transfer);

                // Small delay between chunks to avoid flooding
                await Task.Delay(5);
            }

            transfer.Status = TransferStatus.Completed;
            transfer.Progress = 100;
            TransferCompleted?.Invoke(transfer);

            Log.Information("File '{FileName}' sent successfully in {Elapsed}s",
                transfer.FileName, sw.Elapsed.TotalSeconds);
        }
        catch (Exception ex)
        {
            transfer.Status = TransferStatus.Failed;
            Log.Error(ex, "Error sending file '{FileName}'", transfer.FileName);
            TransferFailed?.Invoke(transfer, ex.Message);
        }

        return transfer;
    }

    // ---- Receiving side ----

    private void OnFileDataReceived(byte[] rawData)
    {
        try
        {
            var packet = PacketBuilder.Deserialize(rawData);
            if (packet is null) return;

            // Decrypt payload
            packet.Payload = EncryptionService.Decrypt(packet.Payload);

            switch (packet.Type)
            {
                case PacketType.FileMetadata:
                    HandleFileMetadata(packet);
                    break;
                case PacketType.FileChunk:
                    HandleFileChunk(packet);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing incoming file data");
        }
    }

    private void HandleFileMetadata(Packet packet)
    {
        // Parse: [16 bytes fileId][8 bytes fileSize][4 bytes totalChunks][UTF8 fileName]
        if (packet.Payload.Length < 28) return;

        var fileId = new Guid(packet.Payload.AsSpan(0, 16));
        var fileSize = BitConverter.ToInt64(packet.Payload, 16);
        var totalChunks = BitConverter.ToInt32(packet.Payload, 24);
        var fileName = Encoding.UTF8.GetString(packet.Payload, 28, packet.Payload.Length - 28);
        var senderId = new Guid(packet.SenderId);

        var downloadsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "meshIt");
        Directory.CreateDirectory(downloadsDir);
        var destPath = Path.Combine(downloadsDir, fileName);

        // Avoid overwriting
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        var counter = 1;
        while (File.Exists(destPath))
        {
            destPath = Path.Combine(downloadsDir, $"{baseName} ({counter++}){ext}");
        }

        var transfer = new FileTransfer
        {
            FileId = fileId,
            FileName = fileName,
            FileSize = fileSize,
            TotalChunks = totalChunks,
            IsOutgoing = false,
            PeerId = senderId,
            Status = TransferStatus.InProgress,
            LocalPath = destPath
        };

        _incoming[fileId] = new IncomingTransfer(transfer, destPath, fileSize);
        TransferStarted?.Invoke(transfer);

        Log.Information("Receiving file '{FileName}' ({Size} bytes, {Chunks} chunks)",
            fileName, fileSize, totalChunks);
    }

    private async void HandleFileChunk(Packet packet)
    {
        if (packet.Payload.Length < 17) return;

        var fileId = new Guid(packet.Payload.AsSpan(0, 16));
        var chunkData = packet.Payload.AsMemory(16).ToArray();

        if (!_incoming.TryGetValue(fileId, out var incoming))
        {
            Log.Warning("Received chunk for unknown transfer {FileId}", fileId);
            return;
        }

        try
        {
            var chunkIndex = incoming.Transfer.TransferredChunks;
            await ChunkHelper.WriteChunkAsync(incoming.DestPath, chunkIndex, chunkData, incoming.TotalFileSize);

            incoming.Transfer.TransferredChunks = chunkIndex + 1;
            incoming.Transfer.Progress = (double)(chunkIndex + 1) / incoming.Transfer.TotalChunks * 100;
            TransferProgress?.Invoke(incoming.Transfer);

            // Check if complete
            if (incoming.Transfer.TransferredChunks >= incoming.Transfer.TotalChunks)
            {
                incoming.Transfer.Status = TransferStatus.Completed;
                incoming.Transfer.Progress = 100;
                _incoming.TryRemove(fileId, out _);
                TransferCompleted?.Invoke(incoming.Transfer);
                Log.Information("File '{FileName}' received successfully → {Path}",
                    incoming.Transfer.FileName, incoming.DestPath);
            }
        }
        catch (Exception ex)
        {
            incoming.Transfer.Status = TransferStatus.Failed;
            TransferFailed?.Invoke(incoming.Transfer, ex.Message);
            Log.Error(ex, "Error writing chunk for '{FileName}'", incoming.Transfer.FileName);
        }
    }

    // Internal bookkeeping record
    private sealed record IncomingTransfer(FileTransfer Transfer, string DestPath, long TotalFileSize);
}
