using System.Windows;
using System.Windows.Input;
using SchoolManager.App.Logging;
using SchoolManager.App.Online;

namespace SchoolManager.App.Dialogs;

/// <summary>
/// Anmeldung für den Entwicklermodus. Geprüft wird beim Server; erst wenn er
/// das Konto annimmt, schliesst sich das Fenster mit Erfolg.
/// </summary>
public partial class DevSignInDialog : Window
{
    public DevSignInDialog()
    {
        InitializeComponent();

        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Loaded += (_, _) => UserNameBox.Focus();
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        var userName = UserNameBox.Text.Trim();

        if (userName.Length == 0 || PasswordBox.Password.Length == 0)
        {
            Fail("Bitte Benutzername und Passwort eingeben.");
            (userName.Length == 0 ? (IInputElement)UserNameBox : PasswordBox).Focus();
            return;
        }

        SignInButton.IsEnabled = false;
        ErrorText.Visibility = Visibility.Collapsed;
        Cursor = Cursors.Wait;

        try
        {
            await DevMode.SignInAsync(userName, PasswordBox.Password);
            DialogResult = true;
        }
        catch (ServerException ex)
        {
            // Ein falsches Passwort ist kein Fehler der Anwendung; nur was am
            // Server oder an der Verbindung hängt, gehört ins Protokoll.
            if (ex.IsUnreachable || (int?)ex.Status >= 500)
                AppLog.Error($"Anmeldung im Entwicklermodus: {ex.Message}", "Server");

            Fail(ex.Message);
            PasswordBox.Clear();
            PasswordBox.Focus();
        }
        finally
        {
            SignInButton.IsEnabled = true;
            Cursor = Cursors.Arrow;
        }
    }

    private void Fail(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
