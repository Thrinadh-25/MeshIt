using System.Windows;
using System.Windows.Input;
using meshIt.ViewModels;
using meshIt.Views;

namespace meshIt;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Show Bluetooth diagnostic dialog on startup
        var btDialog = new BluetoothDiagnosticDialog { Owner = this };
        if (btDialog.ShowDialog() != true)
        {
            // User chose not to continue (Bluetooth unavailable or cancelled)
            _viewModel.IsBleAvailable = false;
            _viewModel.StatusText = "⚠ Bluetooth check skipped or unavailable";
        }

        if (string.IsNullOrWhiteSpace(_viewModel.Username))
        {
            var dialog = new UserSetupDialog { Owner = this };
            if (dialog.ShowDialog() == true)
                await _viewModel.SetUsernameCommand.ExecuteAsync(dialog.EnteredUsername);
            else
            {
                Close();
                return;
            }
        }
        else
        {
            await _viewModel.InitializeBleCommand.ExecuteAsync(null);
        }
    }

    private void OnClosed(object? sender, EventArgs e) => _viewModel.Dispose();

    private void IdentityHeader_Click(object sender, MouseButtonEventArgs e)
    {
        _viewModel.OnLogoTapped(); // Emergency wipe on triple-tap
        _viewModel.ToggleIdentityCommand.Execute(null);
    }

    // ---- Drag & Drop ----

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            _viewModel.IsDragOver = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        _viewModel.IsDragOver = false;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        _viewModel.IsDragOver = false;

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            await _viewModel.SendDroppedFilesCommand.ExecuteAsync(files);
        }
    }
}
