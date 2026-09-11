using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using SchoolManager.App.Data;
using SchoolManager.App.Update;
using SchoolManager.Core;

namespace SchoolManager.App.Pages;

/// <summary>Seite für Konto-Art, Server, Anmeldung, Absender, Programm-Update und Datenexport.</summary>
public partial class SettingsPage : UserControl
{
    private readonly SmtpSettingsService settingsService;
    private readonly IStatusSink status;

    /// <summary>Verhindert, dass das Füllen der Felder als Eingabe zählt.</summary>
    private bool loading;

    /// <summary>Zuletzt gefundene, noch nicht installierte Version.</summary>
    private UpdateInfo? pendingUpdate;

    /// <summary>
    /// Das bei Microsoft angemeldete Postfach. Es wird nicht eingetippt, sondern
    /// kommt aus der Anmeldung im Browser - deshalb ein Feld statt eines
    /// Eingabefeldes auf der Seite.
    /// </summary>
    private string oauthMailbox = "";

    public SettingsPage(SmtpSettingsService settingsService, IStatusSink status)
    {
        this.settingsService = settingsService;
        this.status = status;

        InitializeComponent();

        AccountKindBox.ItemsSource = AccountChoice.Available(settingsService.Current.AccountKind);
        SecurityBox.ItemsSource = SecurityChoice.All;
        SettingsPathText.Text = $"Gespeichert unter {SettingsStore.FilePath}";
        VersionText.Text = UpdateService.CurrentVersion.ToString(3);

        ShowInstallState();
        ShowDevSection();
        DevMode.Changed += ShowDevSection;

        ShowSettings(settingsService.Current);
    }

    /// <summary>Wird von der Seitennavigation aufgerufen, wenn die Seite erscheint.</summary>
    public void Activate()
    {
        Dispatcher.InvokeAsync(() => AccountKindBox.Focus(), DispatcherPriority.Loaded);

        // Der Fokus loest ein verzoegertes BringIntoView aus; mit Background-
        // Prioritaet laeuft der Ruecksprung nach oben zuverlaessig danach.
        Dispatcher.InvokeAsync(() => SettingsScroller.ScrollToTop(), DispatcherPriority.Background);

        _ = ShowSignInStateAsync();
    }

    private MailAccountKind SelectedKind =>
        (AccountKindBox.SelectedItem as AccountChoice)?.Value ?? MailAccountKind.Smtp;

    private void ShowSettings(SmtpSettings value)
    {
        loading = true;

        AccountKindBox.SelectedItem = AccountChoice.All.FirstOrDefault(c => c.Value == value.AccountKind)
                                      ?? AccountChoice.All[0];
        HostBox.Text = value.Host;
        PortBox.Text = value.Port.ToString();
        SecurityBox.SelectedItem = SecurityChoice.All.FirstOrDefault(c => c.Value == value.Security)
                                   ?? SecurityChoice.All[0];
        UserNameBox.Text = value.UserName;
        PasswordInput.Password = value.Password;
        oauthMailbox = value.AccountKind == MailAccountKind.Microsoft365 ? value.UserName : "";
        ClientIdBox.Text = value.ClientId;
        TenantIdBox.Text = value.TenantId;
        FromBox.Text = value.FromAddress;
        DisplayNameBox.Text = value.FromDisplayName;

        loading = false;

        ShowAccountKind();
    }

    /// <summary>Zeigt je Konto-Art die passenden Felder und Hinweise.</summary>
    private void ShowAccountKind()
    {
        var kind = SelectedKind;
        var oauth = kind == MailAccountKind.Microsoft365;

        PasswordPanel.Visibility = oauth ? Visibility.Collapsed : Visibility.Visible;
        OAuthPanel.Visibility = oauth ? Visibility.Visible : Visibility.Collapsed;

        // Microsoft 365 sendet über Graph: kein Server, kein Port, keine
        // Verschlüsselung zum Einstellen - der ganze Abschnitt entfällt.
        ServerPanel.Visibility = oauth ? Visibility.Collapsed : Visibility.Visible;

        // Die Azure-Felder braucht nur, wer keine eingebaute Anwendungs-ID hat.
        AzureFields.Visibility = SmtpSettings.HasBuiltInClientId
            ? Visibility.Collapsed
            : Visibility.Visible;

        AccountHintText.Text = kind switch
        {
            MailAccountKind.Microsoft365 =>
                "Postfach in der Cloud - auch ein privates Microsoft-Konto. Einmal im Browser anmelden, "
                + "fertig: kein Server, kein Passwort. Gesendet wird über Microsoft Graph.",
            MailAccountKind.ExchangeOnPremises =>
                "Exchange-Server der Schule. Host ist zum Beispiel mail.schule.ch, Port 587 mit STARTTLS.",
            _ => "Beliebiger SMTP-Server, etwa smtp.gmail.com oder der Server des Providers."
        };

        PasswordHintText.Text = kind == MailAccountKind.ExchangeOnPremises
            ? "Benutzername je nach Server als DOMÄNE\\benutzer oder als E-Mail-Adresse. Bleibt er leer, wird ohne Anmeldung gesendet."
            : "Bleibt der Benutzername leer, wird ohne Anmeldung gesendet. Das Passwort wird mit der Windows-Datenschutz-API (DPAPI) verschlüsselt abgelegt. Gmail und Outlook brauchen bei Zwei-Faktor-Anmeldung ein App-Passwort.";

        SignInButton.IsEnabled = oauth;
        SignOutButton.IsEnabled = oauth;
    }

    private void AccountKindBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading)
            return;

        // Voreinstellungen der neuen Konto-Art übernehmen.
        var draft = new SmtpSettings { AccountKind = SelectedKind, Host = HostBox.Text.Trim() };
        draft.ApplyDefaultsForAccountKind();

        if (SelectedKind == MailAccountKind.Microsoft365)
        {
            HostBox.Text = draft.Host;
            PortBox.Text = draft.Port.ToString();
        }
        else if (SelectedKind == MailAccountKind.ExchangeOnPremises)
        {
            PortBox.Text = draft.Port.ToString();

            if (string.IsNullOrWhiteSpace(UserNameBox.Text))
                UserNameBox.Text = oauthMailbox;
        }

        if (SelectedKind != MailAccountKind.Smtp)
            SecurityBox.SelectedItem = SecurityChoice.All.First(c => c.Value == SmtpSecurity.StartTls);

        ShowAccountKind();
        _ = ShowSignInStateAsync();
    }

    /// <summary>
    /// Erkennt bei bekannten Anbietern (Gmail, GMX, Bluewin, ...) Server, Port und
    /// Verschlüsselung anhand der eingetippten E-Mail-Adresse - so muss das bei den
    /// gängigsten Postfächern nicht mehr von Hand nachgeschlagen werden.
    /// </summary>
    private void UserNameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (loading)
            return;

        // Solange nicht gespeichert wird, geht nichts verloren - ein erkannter
        // Anbieter darf einen zuvor eingetragenen Server also überschreiben.
        if (!EmailProviderPresets.TryGet(UserNameBox.Text.Trim(), out var preset))
            return;

        HostBox.Text = preset.Host;
        PortBox.Text = preset.Port.ToString();
        SecurityBox.SelectedItem = SecurityChoice.All.First(c => c.Value == preset.Security);

        var domain = UserNameBox.Text.Trim()[(UserNameBox.Text.LastIndexOf('@') + 1)..];
        status.SetStatus($"Servereinstellungen für {domain} automatisch übernommen.", StatusKind.Info);
    }

    /// <summary>Liest die Eingabefelder; gibt null zurück, wenn eine Angabe unbrauchbar ist.</summary>
    private SmtpSettings? ReadSettings()
    {
        if (!int.TryParse(PortBox.Text.Trim(), out var port) || port is < 1 or > 65535)
        {
            PortBox.Focus();
            status.SetStatus("Der Port muss eine Zahl zwischen 1 und 65535 sein.", StatusKind.Error);
            return null;
        }

        var oauth = SelectedKind == MailAccountKind.Microsoft365;

        var value = new SmtpSettings
        {
            AccountKind = SelectedKind,
            Host = HostBox.Text.Trim(),
            Port = port,
            Security = (SecurityBox.SelectedItem as SecurityChoice)?.Value ?? SmtpSecurity.Auto,
            UserName = oauth ? oauthMailbox : UserNameBox.Text.Trim(),
            Password = oauth ? "" : PasswordInput.Password,
            ClientId = ClientIdBox.Text.Trim(),
            TenantId = TenantIdBox.Text.Trim(),
            FromAddress = FromBox.Text.Trim(),
            FromDisplayName = DisplayNameBox.Text.Trim()
        };

        try
        {
            value.Validate();
        }
        catch (Exception ex)
        {
            status.SetStatus(ex.Message, StatusKind.Error);
            return null;
        }

        return value;
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (ReadSettings() is not { } value)
            return;

        try
        {
            settingsService.Update(value);
            status.SetStatus("Einstellungen gespeichert.", StatusKind.Success);
        }
        catch (Exception ex)
        {
            status.SetStatus($"Einstellungen konnten nicht gespeichert werden: {ex.Message}", StatusKind.Error);
        }
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        if (ReadSettings() is not { } value)
            return;

        using var busy = Busy($"Verbindung zu {value.Host}:{value.Port} wird geprüft…");

        try
        {
            await new EmailService(value, SmtpSettingsService.CreateTokenSource(value)).TestConnectionAsync();
            status.SetStatus("Verbindung und Anmeldung erfolgreich.", StatusKind.Success);
        }
        catch (Exception ex)
        {
            status.SetStatus($"Verbindung fehlgeschlagen: {ex.Message}", StatusKind.Error);
        }
    }

    // ==== Microsoft 365 ====

    /// <summary>
    /// Meldet im Browser an und übernimmt das Postfach aus der Anmeldung. Die
    /// Einstellungen werden gleich gespeichert - wer sich anmeldet, will nicht
    /// hinterher noch „Speichern“ drücken müssen.
    /// </summary>
    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        var clientId = TokenSourceClientId();

        if (clientId.Length == 0)
        {
            ClientIdBox.Focus();
            status.SetStatus(
                "Für die Anmeldung fehlt die Anwendungs-ID der Azure-App-Registrierung.",
                StatusKind.Error);
            return;
        }

        using var busy = Busy("Anmeldung bei Microsoft läuft - bitte im Browser fortsetzen…");

        try
        {
            var source = new Microsoft365TokenSource(clientId, TenantIdBox.Text.Trim());
            var signIn = await source.SignInAsync(oauthMailbox);

            oauthMailbox = signIn.Mailbox;

            if (ReadSettings() is { } value)
            {
                settingsService.Update(value);
                status.SetStatus($"Angemeldet als {oauthMailbox}. Der Versand läuft jetzt darüber.",
                    StatusKind.Success);
            }
        }
        catch (Exception ex)
        {
            status.SetStatus($"Anmeldung fehlgeschlagen: {ex.Message}", StatusKind.Error);
        }

        await ShowSignInStateAsync();
    }

    private async void SignOut_Click(object sender, RoutedEventArgs e)
    {
        var clientId = TokenSourceClientId();

        if (clientId.Length == 0)
            return;

        try
        {
            await new Microsoft365TokenSource(clientId, TenantIdBox.Text.Trim()).SignOutAsync();
            oauthMailbox = "";

            if (ReadSettings() is { } value)
                settingsService.Update(value);

            status.SetStatus("Abgemeldet.", StatusKind.Info);
        }
        catch (Exception ex)
        {
            status.SetStatus($"Abmelden fehlgeschlagen: {ex.Message}", StatusKind.Error);
        }

        await ShowSignInStateAsync();
    }

    /// <summary>Die eingebaute Anwendungs-ID, sonst die selbst eingetragene.</summary>
    private string TokenSourceClientId() =>
        SmtpSettings.HasBuiltInClientId ? SmtpSettings.BuiltInClientId : ClientIdBox.Text.Trim();

    private async Task ShowSignInStateAsync()
    {
        if (SelectedKind != MailAccountKind.Microsoft365)
            return;

        var clientId = TokenSourceClientId();

        if (clientId.Length == 0)
        {
            SignInStateText.Text = "Noch nicht angemeldet - zuerst die Anwendungs-ID unten eintragen.";
            SignInButton.IsEnabled = false;
            return;
        }

        SignInButton.IsEnabled = true;

        try
        {
            var source = new Microsoft365TokenSource(clientId, TenantIdBox.Text.Trim());
            var mailbox = await source.SignedInMailboxAsync();

            if (mailbox.Length > 0)
                oauthMailbox = mailbox;

            SignInStateText.Text = mailbox.Length > 0
                ? $"Angemeldet als {mailbox} - der Versand läuft über dieses Postfach."
                : "Noch nicht angemeldet.";

            SignOutButton.IsEnabled = mailbox.Length > 0;
        }
        catch (Exception ex)
        {
            SignInStateText.Text = $"Anmeldestand unbekannt: {ex.Message}";
        }
    }

    // ==== Programm-Update ====

    /// <summary>Zeigt ein beim Start bereits gefundenes Update an, ohne erneut nachzufragen.</summary>
    public void ShowAvailableUpdate(UpdateInfo update)
    {
        pendingUpdate = update;
        UpdateStatusText.Text = $"Version {update.Version} ist verfügbar.";
        InstallUpdateButton.Visibility = Visibility.Visible;
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        InstallUpdateButton.Visibility = Visibility.Collapsed;
        UpdateStatusText.Text = "Suche nach Updates…";
        Cursor = Cursors.Wait;

        try
        {
            pendingUpdate = await UpdateService.CheckForUpdateAsync();

            if (pendingUpdate is null)
            {
                UpdateStatusText.Text = "Sie verwenden bereits die aktuellste Version.";
            }
            else
            {
                UpdateStatusText.Text = $"Version {pendingUpdate.Version} ist verfügbar.";
                InstallUpdateButton.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = $"Suche fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
            Cursor = Cursors.Arrow;
        }
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (pendingUpdate is not { } update)
            return;

        var confirmed = MessageBox.Show(
            Window.GetWindow(this),
            $"Version {update.Version} wird heruntergeladen und installiert. School Manager wird dazu beendet. Fortfahren?",
            "Update installieren",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

        if (!confirmed)
            return;

        InstallUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "Update wird heruntergeladen…";
        Cursor = Cursors.Wait;

        try
        {
            var installerPath = await UpdateService.DownloadAsync(update);
            UpdateService.RunInstallerAndExit(installerPath);
        }
        catch (Exception ex)
        {
            status.SetStatus($"Update konnte nicht heruntergeladen werden: {ex.Message}", StatusKind.Error);
            InstallUpdateButton.IsEnabled = true;
            Cursor = Cursors.Arrow;
        }
    }

    // ==== Datenexport ====

    private void ExportConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Konfiguration exportieren",
            Filter = "JSON-Datei (*.json)|*.json",
            FileName = "schoolmanager-einstellungen.json",
            AddExtension = true,
            DefaultExt = ".json"
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        try
        {
            DataExportService.ExportConfig(dialog.FileName);
            status.SetStatus($"Konfiguration nach {Path.GetFileName(dialog.FileName)} geschrieben.", StatusKind.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            status.SetStatus($"Export fehlgeschlagen: {ex.Message}", StatusKind.Error);
        }
    }

    private void ExportAll_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Alle Daten exportieren",
            Filter = "ZIP-Datei (*.zip)|*.zip",
            FileName = $"schoolmanager-backup-{DateTime.Now:yyyy-MM-dd}.zip",
            AddExtension = true,
            DefaultExt = ".zip"
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        try
        {
            DataExportService.ExportAll(dialog.FileName);
            status.SetStatus($"Alle Daten nach {Path.GetFileName(dialog.FileName)} geschrieben.", StatusKind.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            status.SetStatus($"Export fehlgeschlagen: {ex.Message}", StatusKind.Error);
        }
    }

    private void ImportConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Konfiguration importieren",
            Filter = "JSON-Datei (*.json)|*.json|Alle Dateien (*.*)|*.*"
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        try
        {
            var imported = DataImportService.ImportConfig(dialog.FileName);
            settingsService.Update(imported);
            ShowSettings(imported);
            status.SetStatus(
                "Konfiguration eingelesen. Das Passwort war nicht im Export enthalten und muss neu eingegeben werden.",
                StatusKind.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            status.SetStatus($"Import fehlgeschlagen: {ex.Message}", StatusKind.Error);
        }
    }

    private void ImportAll_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Alle Daten importieren",
            Filter = "ZIP-Datei (*.zip)|*.zip|Alle Dateien (*.*)|*.*"
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        var confirmed = MessageBox.Show(
            Window.GetWindow(this),
            "Bestehende Daten werden mit dem Inhalt der Sicherung überschrieben. School Manager wird danach neu gestartet. Fortfahren?",
            "Alle Daten importieren",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;

        if (!confirmed)
            return;

        try
        {
            DataImportService.ImportAll(dialog.FileName);
            RestartApplication();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            status.SetStatus($"Import fehlgeschlagen: {ex.Message}", StatusKind.Error);
        }
    }

    /// <summary>Startet School Manager neu, ohne das normale Speichern beim Schliessen auszulösen -
    /// das würde sonst die gerade importierten Dateien mit dem alten Stand im Speicher überschreiben.</summary>
    private static void RestartApplication()
    {
        if (Environment.ProcessPath is { } exePath)
            Process.Start(exePath);

        Environment.Exit(0);
    }

    // ==== Entwicklermodus ====

    /// <summary>Zeigt den Entwickler-Abschnitt und die Konto-Auswahl passend an.</summary>
    private void ShowDevSection()
    {
        DevPanel.Visibility = DevMode.IsEnabled ? Visibility.Visible : Visibility.Collapsed;

        loading = true;
        DevPatchesBox.IsChecked = DevMode.UseDevPatches;

        DevBackupText.Text = DevBackupService.Exists(DevMode.BackupPath)
            ? $"Sicherung vom {File.GetLastWriteTime(DevMode.BackupPath!):dd.MM.yyyy HH:mm} unter {DevMode.BackupPath}."
            : "Es liegt keine Sicherung vor - beim Verlassen bleiben die Daten, wie sie sind.";

        // Die Konto-Auswahl kann durch den Modus länger oder kürzer werden.
        var selected = SelectedKind;
        AccountKindBox.ItemsSource = AccountChoice.Available(selected);
        AccountKindBox.SelectedItem = AccountChoice.All.FirstOrDefault(c => c.Value == selected);
        loading = false;
    }

    private void DevPatches_Changed(object sender, RoutedEventArgs e)
    {
        if (loading)
            return;

        DevMode.SetDevPatches(DevPatchesBox.IsChecked == true);

        status.SetStatus(
            DevMode.UseDevPatches
                ? "Die Update-Suche nimmt ab jetzt Dev-Patches."
                : "Die Update-Suche nimmt wieder die öffentlichen Releases.",
            StatusKind.Info);
    }

    /// <summary>
    /// Schaltet den Entwicklermodus ab und bietet dabei den ganzen Weg zurück
    /// an: die letzte öffentliche Version einspielen und die Sicherung von vor
    /// dem Einschalten wiederherstellen. Wer nur den Modus loswerden will,
    /// behält Version und Daten.
    /// </summary>
    private async void LeaveDev_Click(object sender, RoutedEventArgs e)
    {
        var backup = DevMode.BackupPath;

        var restores = DevBackupService.Exists(backup)
            ? $", und die Sicherung vom {File.GetLastWriteTime(backup!):dd.MM.yyyy HH:mm} wird wieder "
              + "eingespielt - alles seither Erfasste geht dabei verloren"
            : " (eine Sicherung liegt nicht vor, die Daten bleiben unverändert)";

        var answer = MessageBox.Show(
            Window.GetWindow(this),
            "Soll dabei der Stand von vor dem Entwicklermodus wiederhergestellt werden?\n\n"
            + $"Ja: Die letzte öffentliche Version wird installiert{restores}. School Manager wird "
            + "dazu beendet und startet danach neu.\n\n"
            + "Nein: Nur der Entwicklermodus wird abgeschaltet; Version und Daten bleiben, wie sie sind.",
            "Entwicklermodus verlassen",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);

        if (answer == MessageBoxResult.Cancel)
            return;

        if (answer == MessageBoxResult.No)
        {
            DevMode.Disable();
            ShowDevSection();
            status.SetStatus("Entwicklermodus abgeschaltet.", StatusKind.Info);
            return;
        }

        LeaveDevButton.IsEnabled = false;
        Cursor = Cursors.Wait;
        status.SetStatus("Die letzte öffentliche Version wird geholt…", StatusKind.Info);

        try
        {
            var release = await UpdateService.LatestReleaseAsync()
                          ?? throw new InvalidOperationException(
                              "Es ist keine öffentliche Version mit Installationspaket vorhanden.");

            var installerPath = await UpdateService.DownloadAsync(release);

            DevMode.Disable();
            DevBackupService.RestoreAndExit(installerPath, backup);
        }
        catch (Exception ex)
        {
            status.SetStatus($"Zurücksetzen fehlgeschlagen: {ex.Message}", StatusKind.Error);
            LeaveDevButton.IsEnabled = true;
            Cursor = Cursors.Arrow;
        }
    }

    // ==== Zurücksetzen und Deinstallieren ====

    /// <summary>
    /// Löscht alle eigenen Daten. Weil die Seiten ihre Eingaben beim Schliessen
    /// nochmals speichern, räumt ein Skript nach dem Beenden auf und startet
    /// School Manager danach neu.
    /// </summary>
    private void ResetData_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = MessageBox.Show(
            Window.GetWindow(this),
            "Alle eigenen Daten werden gelöscht: Aufträge, Aufgaben, Hausaufgaben, Prüfungen, "
            + "Lehrkräfte, Stundenplan, To-Do, Notizen und die Einstellungen samt Anmeldung.\n\n"
            + $"Gelöscht wird der Ordner {UninstallService.DataFolder}.\n\n"
            + "School Manager startet danach neu und beginnt leer. Das lässt sich nicht rückgängig "
            + "machen - ohne vorherige Sicherung über „Alle Daten exportieren…“ sind die Daten weg.\n\n"
            + "Jetzt zurücksetzen?",
            "Daten zurücksetzen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;

        if (!confirmed)
            return;

        try
        {
            UninstallService.ResetDataAndRestart();
        }
        catch (Exception ex)
        {
            status.SetStatus($"Zurücksetzen fehlgeschlagen: {ex.Message}", StatusKind.Error);
        }
    }

    /// <summary>Entfernt Installation und Daten; die App beendet sich dabei.</summary>
    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        var removed = string.Join("\n", UninstallService.WhatWillBeRemoved().Select(item => $"• {item}"));

        var confirmed = MessageBox.Show(
            Window.GetWindow(this),
            "School Manager wird vollständig vom Rechner entfernt. Gelöscht werden:\n\n"
            + removed
            + "\n\nSchool Manager wird dazu beendet. Das lässt sich nicht rückgängig machen.\n\n"
            + "Jetzt deinstallieren?",
            "School Manager deinstallieren",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;

        if (!confirmed)
            return;

        try
        {
            UninstallService.UninstallAndExit();
        }
        catch (Exception ex)
        {
            status.SetStatus($"Deinstallation fehlgeschlagen: {ex.Message}", StatusKind.Error);
        }
    }

    /// <summary>Sagt, ob School Manager installiert ist oder als Kopie läuft.</summary>
    private void ShowInstallState()
    {
        UninstallStateText.Text = UninstallService.Installed is { } installation
            ? $"Installiert in {installation.Location}. Die Deinstallation ruft das Setup auf "
              + "und entfernt danach auch den Datenordner."
            : $"School Manager läuft als einzelne Datei ({UninstallService.ProgramPath}), es gibt "
              + "keine Installation. Die Deinstallation löscht diese Datei und den Datenordner.";
    }

    // ==== Oberfläche ====

    /// <summary>Sperrt die Schaltflächen für die Dauer einer Netzwerk-Aktion.</summary>
    private IDisposable Busy(string message)
    {
        status.SetStatus(message, StatusKind.Info);

        TestConnectionButton.IsEnabled = false;
        SaveSettingsButton.IsEnabled = false;
        SignInButton.IsEnabled = false;
        Cursor = Cursors.Wait;

        return new BusyScope(this);
    }

    private sealed class BusyScope(SettingsPage page) : IDisposable
    {
        public void Dispose()
        {
            page.TestConnectionButton.IsEnabled = true;
            page.SaveSettingsButton.IsEnabled = true;
            page.SignInButton.IsEnabled = page.SelectedKind == MailAccountKind.Microsoft365;
            page.Cursor = Cursors.Arrow;
        }
    }

    /// <summary>Ein Eintrag der Konto-Auswahl.</summary>
    public sealed class AccountChoice(string label, MailAccountKind value)
    {
        public MailAccountKind Value { get; } = value;

        public override string ToString() => label;

        /// <summary>
        /// Die auswählbaren Konto-Arten. Microsoft 365 ist noch nicht
        /// einsatzbereit - ohne eingebaute Anwendungs-ID führt es nur in eine
        /// Sackgasse - und erscheint deshalb nur im Entwicklermodus. Wer es
        /// bereits eingestellt hat, behält es aber sichtbar.
        /// </summary>
        public static AccountChoice[] Available(MailAccountKind current) =>
            All.Where(choice => choice.Value != MailAccountKind.Microsoft365
                                || DevMode.IsEnabled
                                || current == MailAccountKind.Microsoft365)
                .ToArray();

        public static AccountChoice[] All { get; } =
        [
            new("SMTP-Server", MailAccountKind.Smtp),
            new("Exchange-Server im Haus", MailAccountKind.ExchangeOnPremises),
            new("Microsoft 365 / Exchange Online", MailAccountKind.Microsoft365)
        ];
    }

    /// <summary>Ein Eintrag der Verschlüsselungs-Auswahl.</summary>
    public sealed class SecurityChoice(string label, SmtpSecurity value)
    {
        public SmtpSecurity Value { get; } = value;

        public override string ToString() => label;

        public static SecurityChoice[] All { get; } =
        [
            new("Automatisch (nach Port)", SmtpSecurity.Auto),
            new("STARTTLS (Port 587)", SmtpSecurity.StartTls),
            new("SSL/TLS (Port 465)", SmtpSecurity.SslOnConnect),
            new("Ohne Verschlüsselung", SmtpSecurity.None)
        ];
    }
}
