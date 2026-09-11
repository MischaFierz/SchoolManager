using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using SchoolManager.App.Logging;

namespace SchoolManager.App.Pages;

/// <summary>
/// Das Protokoll: aufgezeichnete Fehler und der Changelog dieser Fassung.
///
/// Die Seite erscheint nur im Entwicklermodus in der Navigation. Sie ist
/// bewusst kein Teil der Einstellungen mehr - wer einen Fehler sucht, will
/// nicht erst durch Server, Anmeldung und Datenexport blättern.
/// </summary>
public partial class LogPage : UserControl
{
    private readonly IStatusSink status;

    public LogPage(IStatusSink status)
    {
        this.status = status;

        InitializeComponent();

        ShowLog();
        ShowChangelog();

        AppLog.Changed += ShowLog;
    }

    /// <summary>Wird von der Seitennavigation aufgerufen, wenn die Seite erscheint.</summary>
    public void Activate()
    {
        ShowLog();
        ShowChangelog();
    }

    /// <summary>
    /// Zeigt die aufgezeichneten Fehler. Aufgezeichnet wird auch aus anderen
    /// Fäden heraus - darum der Umweg über den Faden des Fensters.
    /// </summary>
    private void ShowLog()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(ShowLog);
            return;
        }

        ErrorList.ItemsSource = AppLog.Entries;
        ErrorsEmptyText.Visibility = AppLog.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        LogStateText.Text = AppLog.Count switch
        {
            0 => $"Nichts aufgezeichnet. Geschrieben wird nach {AppLog.FilePath}",
            1 => $"1 Eintrag · {AppLog.FilePath}",
            var count => $"{count} Einträge · {AppLog.FilePath}"
        };

        ShowTabHint();
    }

    /// <summary>Zeigt, was diese Vorabversion gegenüber der letzten öffentlichen bringt.</summary>
    private void ShowChangelog()
    {
        var since = Changelog.SinceLastPublic;

        ChangelogList.ItemsSource = since;
        ChangelogEmptyText.Visibility = since.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        ChangelogHeaderText.Text = since.Count == 0
            ? ""
            : Changelog.LastPublic is { } last
                ? $"Neu gegenüber der öffentlichen Version {last.Version} vom {last.Date}:"
                : "Neu in dieser Vorabversion:";
    }

    /// <summary>Der Hinweis neben den Reitern sagt, was der andere zu bieten hat.</summary>
    private void ShowTabHint() =>
        TabHintText.Text = ErrorsTab.IsChecked == true
            ? "Jede Fehlermeldung der Anwendung, die neueste zuoberst."
            : "Was diese Fassung seit der letzten öffentlichen Version bringt.";

    private void LogTab_Checked(object sender, RoutedEventArgs e)
    {
        // Beim Aufbau der Seite steht der Reiter schon, die Felder noch nicht.
        if (ErrorsPanel is null || ChangelogPanel is null)
            return;

        var errors = ErrorsTab.IsChecked == true;

        ErrorsPanel.Visibility = errors ? Visibility.Visible : Visibility.Collapsed;
        ChangelogPanel.Visibility = errors ? Visibility.Collapsed : Visibility.Visible;

        ShowTabHint();
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        if (AppLog.Count == 0)
        {
            status.SetStatus("Es ist nichts aufgezeichnet, was sich kopieren liesse.", StatusKind.Info);
            return;
        }

        try
        {
            Clipboard.SetText(AppLog.AsText());
            status.SetStatus($"{AppLog.Count} Einträge in die Zwischenablage kopiert.", StatusKind.Success);
        }
        catch (Exception ex)
        {
            // Die Zwischenablage gehört dem ganzen System; sie ist manchmal belegt.
            status.SetStatus($"Kopieren fehlgeschlagen: {ex.Message}", StatusKind.Error);
        }
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(AppLog.FilePath))
        {
            status.SetStatus("Es gibt noch keine Protokolldatei.", StatusKind.Info);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(AppLog.FilePath) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Für .log ist nicht überall ein Programm hinterlegt; dann eben der Ordner.
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{AppLog.FilePath}\""));
        }
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        AppLog.Clear();
        status.SetStatus("Das Protokoll ist geleert.", StatusKind.Info);
    }
}
