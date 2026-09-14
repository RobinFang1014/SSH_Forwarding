using System.Windows;

namespace BIOSForwardManager;

public partial class LoginDialog : Window
{
    public string Password => PasswordBox.Password;
    public string TotpCode => TotpBox.Text.Trim();

    public LoginDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => PasswordBox.Focus();
    }

    private void OnConnect(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(PasswordBox.Password))
        {
            ErrorText.Text = "Password is required.";
            return;
        }
        if (string.IsNullOrEmpty(TotpBox.Text.Trim()))
        {
            ErrorText.Text = "2FA code is required.";
            return;
        }
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
