using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using meshIt.Models;
using meshIt.Services;

namespace meshIt.ViewModels;

/// <summary>
/// ViewModel for the channel list sidebar, available channels, and channel chat.
/// </summary>
public partial class ChannelListViewModel : ObservableObject
{
    private readonly ChannelService _channelService;

    public ObservableCollection<Channel> Channels => _channelService.JoinedChannels;
    public ObservableCollection<Channel> AvailableChannels => _channelService.AvailableChannels;
    public ObservableCollection<Message> ChannelMessages { get; } = new();

    [ObservableProperty] private Channel? _selectedChannel;
    [ObservableProperty] private string _channelInput = string.Empty;

    public ChannelListViewModel(ChannelService channelService)
    {
        _channelService = channelService;
    }

    [RelayCommand]
    private void JoinChannel()
    {
        if (string.IsNullOrWhiteSpace(ChannelInput)) return;
        _channelService.JoinChannel(ChannelInput.Trim());
        ChannelInput = string.Empty;
    }

    [RelayCommand]
    private void JoinAvailableChannel(string channelName)
    {
        if (string.IsNullOrWhiteSpace(channelName)) return;
        _channelService.JoinChannel(channelName);
    }

    [RelayCommand]
    private void LeaveChannel(string channelName)
    {
        _channelService.LeaveChannel(channelName);
    }
}
