using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using meshIt.Models;
using meshIt.Services;

namespace meshIt.ViewModels;

/// <summary>
/// ViewModel for the identity/fingerprint display and QR verification.
/// </summary>
public partial class IdentityViewModel : ObservableObject
{
    private readonly IdentityService _identityService;
    private readonly VerificationService _verificationService;
    private readonly TrustService _trustService;

    [ObservableProperty] private string _nickname = string.Empty;
    [ObservableProperty] private string _shortFingerprint = string.Empty;
    [ObservableProperty] private string _fullFingerprint = string.Empty;
    [ObservableProperty] private ImageSource? _qrCodeImage;
    [ObservableProperty] private string _verifyInput = string.Empty;
    [ObservableProperty] private string _verifyResult = string.Empty;

    public IdentityViewModel(
        IdentityService identityService,
        VerificationService verificationService,
        TrustService trustService)
    {
        _identityService = identityService;
        _verificationService = verificationService;
        _trustService = trustService;
        LoadIdentity();
    }

    private void LoadIdentity()
    {
        var id = _identityService.CurrentIdentity;
        if (id is null) return;

        Nickname = id.Nickname;
        ShortFingerprint = id.ShortFingerprint;
        FullFingerprint = id.Fingerprint;

        // Generate QR code
        var pngBytes = _verificationService.GenerateQrCodePng(id);
        QrCodeImage = LoadPngFromBytes(pngBytes);
    }

    [RelayCommand]
    private void VerifyFingerprint()
    {
        if (string.IsNullOrWhiteSpace(VerifyInput))
        {
            VerifyResult = "Enter a fingerprint to verify.";
            return;
        }

        var fp = VerifyInput.Trim().ToLower().Replace(" ", "");
        _trustService.SetVerified(fp);
        VerifyResult = $"✅ Fingerprint {fp[..8]}… marked as verified!";
        VerifyInput = string.Empty;
    }

    private static ImageSource? LoadPngFromBytes(byte[] pngBytes)
    {
        try
        {
            var image = new BitmapImage();
            using var ms = new MemoryStream(pngBytes);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = ms;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }
}
