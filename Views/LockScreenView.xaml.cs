using System.Windows;
using System.Windows.Input;
using meshIt.Services;

namespace meshIt.Views;

public partial class LockScreenView : Window
{
    private readonly ScreenLockService _lockService;

    /// <summary>Fired when the user successfully unlocks.</summary>
    public event Action? Unlocked;

    public LockScreenView(ScreenLockService lockService)
    {
        InitializeComponent();
        _lockService = lockService;
        PinBox.Focus();
    }

    private void PinBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            TryUnlock();
    }

    private void Unlock_Click(object sender, RoutedEventArgs e) => TryUnlock();

    private void TryUnlock()
    {
        var pin = PinBox.Password;
        if (_lockService.VerifyPin(pin))
        {
            Unlocked?.Invoke();
            Close();
        }
        else
        {
            ErrorText.Text = "Incorrect PIN. Try again.";
            PinBox.Clear();
            PinBox.Focus();
        }
    }
}
