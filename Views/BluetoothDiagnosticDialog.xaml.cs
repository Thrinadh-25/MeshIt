using System.Windows;
using System.Windows.Media;
using meshIt.Services;

namespace meshIt.Views;

/// <summary>
/// Startup dialog that checks Bluetooth hardware availability and displays
/// detailed diagnostics. Shown before BLE initialization begins.
/// </summary>
public partial class BluetoothDiagnosticDialog : Window
{
    /// <summary>Whether Bluetooth is available and the app should proceed.</summary>
    public bool BluetoothReady { get; private set; }

    public BluetoothDiagnosticDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RunDiagnostics();
    }

    private void RunDiagnostics()
    {
        var diag = BleAvailabilityChecker.GetDiagnostics();

        // Fill in values
        AdapterValue.Text = diag.AdapterName;
        AddressValue.Text = diag.AdapterAddress;
        ModeValue.Text = diag.RadioMode;

        if (diag.IsReady)
        {
            // Green status
            StatusBanner.Background = new SolidColorBrush(Color.FromRgb(0x0d, 0x3b, 0x2e));
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x00, 0xb8, 0x94));
            StatusLabel.Text = "Bluetooth is ready!";
            StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xb8, 0x94));
            ContinueButton.IsEnabled = true;
            ErrorPanel.Visibility = Visibility.Collapsed;
            BluetoothReady = true;
        }
        else
        {
            // Red status
            StatusBanner.Background = new SolidColorBrush(Color.FromRgb(0x3b, 0x0d, 0x0d));
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xff, 0x6b, 0x6b));
            StatusLabel.Text = "Bluetooth is not available";
            StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0x6b, 0x6b));
            ContinueButton.IsEnabled = false;
            BluetoothReady = false;

            if (!string.IsNullOrEmpty(diag.ErrorMessage))
            {
                ErrorPanel.Visibility = Visibility.Visible;
                ErrorText.Text = diag.ErrorMessage;
            }
        }
    }

    private void OnRetryClick(object sender, RoutedEventArgs e)
    {
        RunDiagnostics();
    }

    private void OnContinueClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
