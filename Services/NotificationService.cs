using Microsoft.Toolkit.Uwp.Notifications;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// Windows 10/11 toast notifications for incoming messages, file transfers, etc.
/// </summary>
public class NotificationService
{
    private bool _enabled = true;

    public bool IsEnabled { get => _enabled; set => _enabled = value; }

    /// <summary>Show a toast notification for a new message.</summary>
    public void ShowMessageNotification(string sender, string message, Guid peerId)
    {
        if (!_enabled) return;

        try
        {
            new ToastContentBuilder()
                .AddText(sender)
                .AddText(message.Length > 100 ? message[..100] + "…" : message)
                .AddButton(new ToastButton()
                    .SetContent("Reply")
                    .AddArgument("action", "reply")
                    .AddArgument("peerId", peerId.ToString()))
                .AddButton(new ToastButton()
                    .SetContent("Dismiss")
                    .SetDismissActivation())
                .SetToastDuration(ToastDuration.Short)
                .Show();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to show toast notification");
        }
    }

    /// <summary>Show a notification for completed file transfer.</summary>
    public void ShowFileNotification(string peerName, string fileName, bool received)
    {
        if (!_enabled) return;

        try
        {
            var action = received ? "received from" : "sent to";
            new ToastContentBuilder()
                .AddText($"File {action} {peerName}")
                .AddText(fileName)
                .Show();
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
            new ToastContentBuilder()
                .AddText($"{peerName} is nearby")
                .AddText("Tap to start chatting")
                .SetToastDuration(ToastDuration.Short)
                .Show();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to show peer notification");
        }
    }
}
