using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using SchoolManager.App.Data;
using SchoolManager.Core;

namespace SchoolManager.App.Pages;

/// <summary>Seite zum Verfassen und Verschicken einer E-Mail.</summary>
public partial class MailPage : UserControl
{
    /// <summary>Erster Eintrag der Lehrkraft-Auswahl.</summary>
    private const string PickPrompt = "Lehrkraft einfügen…";

    private readonly ObservableCollection<AttachmentItem> attachments = [];
    private readonly SmtpSettingsService settingsService;
    private readonly TeacherStore teachers;
    private readonly IStatusSink status;

    /// <summary>Verhindert, dass das Zurücksetzen der Auswahl als Eingabe zählt.</summary>
    private bool suppressTeacherChange;

    public MailPage(SmtpSettingsService settingsService, TeacherStore teachers, IStatusSink status)
    {
        this.settingsService = settingsService;
        this.teachers = teachers;
        this.status = status;

        InitializeComponent();

        AttachmentList.ItemsSource = attachments;
        AttachmentList.SelectionChanged += (_, _) =>
            RemoveAttachmentButton.IsEnabled = AttachmentList.SelectedItem is not null;

        settingsService.Changed += ShowSender;
        teachers.Changed += ShowTeachers;

        ShowSender();
        ShowTeachers();
    }

    /// <summary>Wird von der Seitennavigation aufgerufen, wenn die Seite erscheint.</summary>
    public void Activate()
    {
        ShowTeachers();
        ToBox.Focus();
    }

    /// <summary>Setzt eine Lehrkraft als Empfänger - etwa von der Lehrkräfte-Seite aus.</summary>
    public void AddRecipient(Teacher teacher)
    {
        AddAddress(teacher.MailAddress);
        SubjectBox.Focus();
    }

    // ==== Lehrkräfte als Empfänger ====

    /// <summary>Füllt die Auswahlliste mit den Lehrkräften, die eine Adresse haben.</summary>
    private void ShowTeachers()
    {
        var items = new List<object> { PickPrompt };
        items.AddRange(teachers.Items.Where(teacher => teacher.HasEmail));

        suppressTeacherChange = true;
        TeacherBox.ItemsSource = items;
        TeacherBox.SelectedIndex = 0;
        suppressTeacherChange = false;

        TeacherBox.IsEnabled = items.Count > 1;

        TeacherBox.ToolTip = items.Count > 1
            ? "Lehrkraft als Empfänger einfügen"
            : "Noch keine Lehrkraft mit E-Mail-Adresse erfasst - siehe Seite Lehrkräfte";
    }

    private void TeacherBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressTeacherChange || TeacherBox.SelectedItem is not Teacher teacher)
            return;

        AddAddress(teacher.MailAddress);

        // Wieder auf den Hinweis zurückstellen, damit man mehrere wählen kann.
        suppressTeacherChange = true;
        TeacherBox.SelectedIndex = 0;
        suppressTeacherChange = false;
    }

    /// <summary>Hängt eine Adresse an das Empfängerfeld an, ohne sie zu doppeln.</summary>
    private void AddAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return;

        var current = ToBox.Text.Trim();

        if (current.Contains(address, StringComparison.OrdinalIgnoreCase))
        {
            status.SetStatus("Diese Adresse steht schon im Empfängerfeld.", StatusKind.Info);
            return;
        }

        ToBox.Text = current.Length == 0
            ? address
            : $"{current.TrimEnd(',', ' ')}, {address}";

        status.SetStatus($"{address} als Empfänger eingefügt.", StatusKind.Success);
    }

    private void ShowSender()
    {
        var settings = settingsService.Current;

        SenderText.Text = settingsService.IsConfigured
            ? $"Von {settings.EffectiveFrom} über {settings.Host}:{settings.Port}"
            : "Noch kein SMTP-Server eingerichtet";
    }

    private void CopyToggle_Changed(object sender, RoutedEventArgs e)
    {
        var visibility = CopyToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        CcLabel.Visibility = visibility;
        CcBox.Visibility = visibility;
        BccLabel.Visibility = visibility;
        BccBox.Visibility = visibility;

        if (visibility == Visibility.Visible)
            CcBox.Focus();
    }

    private void AddAttachment_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Anhänge auswählen",
            Multiselect = true
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        foreach (var path in dialog.FileNames)
        {
            if (attachments.Any(a => string.Equals(a.FullPath, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            attachments.Add(new AttachmentItem(path));
        }
    }

    private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (AttachmentList.SelectedItem is AttachmentItem item)
            attachments.Remove(item);
    }

    private async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync();

    /// <summary>Strg+Enter verschickt die Nachricht; wird vom Hauptfenster weitergegeben.</summary>
    public async Task SendShortcutAsync()
    {
        if (SendButton.IsEnabled)
            await SendAsync();
    }

    private async Task SendAsync()
    {
        if (!settingsService.IsConfigured)
        {
            status.SetStatus("Es ist kein SMTP-Server eingerichtet - siehe Einstellungen.", StatusKind.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(ToBox.Text) &&
            string.IsNullOrWhiteSpace(CcBox.Text) &&
            string.IsNullOrWhiteSpace(BccBox.Text))
        {
            ToBox.Focus();
            status.SetStatus("Bitte mindestens einen Empfänger angeben.", StatusKind.Error);
            return;
        }

        var mail = new OutgoingEmail
        {
            Subject = SubjectBox.Text,
            Body = BodyBox.Text,
            IsHtml = HtmlCheck.IsChecked == true
        };

        mail.To.AddRange(SplitAddresses(ToBox.Text));
        mail.Cc.AddRange(SplitAddresses(CcBox.Text));
        mail.Bcc.AddRange(SplitAddresses(BccBox.Text));
        mail.Attachments.AddRange(attachments.Select(a => a.FullPath));

        SendButton.IsEnabled = false;
        Cursor = Cursors.Wait;
        status.SetStatus("E-Mail wird gesendet…", StatusKind.Info);

        try
        {
            await new EmailService(settingsService.Current, settingsService.CreateTokenSource()).SendAsync(mail);

            var recipients = mail.To.Concat(mail.Cc).Concat(mail.Bcc);
            status.SetStatus($"Gesendet an {string.Join(", ", recipients)}", StatusKind.Success);
            ClearMessage();
        }
        catch (Exception ex)
        {
            status.SetStatus($"Senden fehlgeschlagen: {ex.Message}", StatusKind.Error);
        }
        finally
        {
            SendButton.IsEnabled = true;
            Cursor = Cursors.Arrow;
        }
    }

    private void ClearMessage()
    {
        ToBox.Clear();
        CcBox.Clear();
        BccBox.Clear();
        SubjectBox.Clear();
        BodyBox.Clear();
        attachments.Clear();
        ToBox.Focus();
    }

    private static IEnumerable<string> SplitAddresses(string value) =>
        value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Ein Anhang, in der Liste mit Dateiname und vollem Pfad als Tooltip.</summary>
    public sealed class AttachmentItem(string fullPath)
    {
        public string FullPath { get; } = fullPath;
        public string FileName { get; } = Path.GetFileName(fullPath);
    }
}
