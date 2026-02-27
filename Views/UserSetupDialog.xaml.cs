using System.Windows;

namespace meshIt.Views;

public partial class UserSetupDialog : Window
{
    /// <summary>The display name the user entered.</summary>
    public string EnteredUsername { get; private set; } = string.Empty;

    public UserSetupDialog()
    {
        InitializeComponent();
        UsernameInput.Focus();
    }

    private void OnGetStartedClicked(object sender, RoutedEventArgs e)
    {
        var name = UsernameInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            UsernameInput.Focus();
            return;
        }

        EnteredUsername = name;
        DialogResult = true;
        Close();
    }
}
