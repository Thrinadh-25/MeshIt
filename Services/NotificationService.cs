using System.Windows;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// Windows toast-style notifications using WPF MessageBox.
/// Replaced UWP Microsoft.Toolkit.Uwp.Notifications with simple WPF approach.
/// For richer notifications, the Hardcodet.NotifyIcon.Wpf tray icon can show balloons.
/// </summary>
public class NotificationService
{
    private bool _enabled = true;

    public bool IsEnabled { get => _enabled; set => _enabled = value; }

    /// <summary>Show a notification for a new message.</summary>
    public void ShowMessageNotification(string sender, string message, Guid peerId)
    {
        if (!_enabled) return;

        try
        {
            var preview = message.Length > 100 ? message[..100] + "…" : message;
            Log.Information("Notification: New message from {Sender}: {Preview}", sender, preview);

            // Use WPF dispatcher to show notification on UI thread
            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                try
                {
                    MessageBox.Show(
                        $"{sender}: {preview}",
                        "meshIt — New Message",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch { }
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to show message notification");
        }
    }

    /// <summary>Show a notification for completed file transfer.</summary>
    public void ShowFileNotification(string peerName, string fileName, bool received)
    {
        if (!_enabled) return;

        try
        {
            var action = received ? "received from" : "sent to";
            Log.Information("Notification: File {Action} {Peer}: {File}", action, peerName, fileName);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to show file notification");
        }
    }

    /// <summary>Show a notification when a peer comes online.</summary>
    public void ShowPeerOnlineNotification(string peerName)
    {
        if (!_enabled) return;

        try
        {
            Log.Information("Notification: {Peer} is nearby", peerName);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to show peer notification");
        }
    }
}
