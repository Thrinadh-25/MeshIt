using CommunityToolkit.Mvvm.ComponentModel;

namespace meshIt.Models;

/// <summary>
/// Current state of a file transfer.
/// </summary>
public enum TransferStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Tracks the progress of an outgoing or incoming file transfer.
/// </summary>
public partial class FileTransfer : ObservableObject
{
    /// <summary>Unique identifier for this transfer session.</summary>
    [ObservableProperty] private Guid _fileId = Guid.NewGuid();

    /// <summary>Original file name (with extension).</summary>
    [ObservableProperty] private string _fileName = string.Empty;

    /// <summary>Total size of the file in bytes.</summary>
    [ObservableProperty] private long _fileSize;

    /// <summary>Total number of chunks the file was split into.</summary>
    [ObservableProperty] private int _totalChunks;

    /// <summary>Number of chunks transferred so far.</summary>
    [ObservableProperty] private int _transferredChunks;

    /// <summary>Progress percentage (0–100).</summary>
    [ObservableProperty] private double _progress;

    /// <summary>Current status of the transfer.</summary>
    [ObservableProperty] private TransferStatus _status = TransferStatus.Pending;

    /// <summary>True if we are the sender, false if we are the receiver.</summary>
    [ObservableProperty] private bool _isOutgoing;

    /// <summary>The peer we are transferring with.</summary>
    [ObservableProperty] private Guid _peerId;

    /// <summary>Transfer speed in bytes per second.</summary>
    [ObservableProperty] private double _speedBytesPerSecond;

    /// <summary>Local file path (for received files: destination; for sent files: source).</summary>
    public string LocalPath { get; set; } = string.Empty;
}
