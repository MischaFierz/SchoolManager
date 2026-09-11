using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SchoolManager.App.Data;

namespace SchoolManager.App.Pages;

/// <summary>
/// Prüfungen erfassen. Jede Prüfung steht sofort im Kalender und lässt sich
/// einzeln oder gesammelt als ICS-Datei ausgeben.
/// </summary>
public partial class ExamsPage : UserControl
{
    private readonly EventStore store;
    private readonly IStatusSink status;

    public ExamsPage(EventStore store, IStatusSink status)
    {
        this.store = store;
        this.status = status;

        InitializeComponent();

        ExamList.ItemsSource = store.Items;

        // Nur Prüfungen zeigen; Termine gehören in den Kalender.
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(ExamList.ItemsSource);
        view.Filter = item => item is CalendarEvent { Kind: CalendarEventKind.Pruefung };

        store.Changed += () =>
        {
            view.Refresh();
            UpdateSummary();
        };

        if (!view.IsEmpty)
            ExamList.SelectedIndex = 0;

        UpdateSummary();
    }

    /// <summary>Bittet die Seitennavigation, den Kalender in dieser Woche zu zeigen.</summary>
    public event Action<DateTime>? ShowInCalendarRequested;

    /// <summary>Wird von der Seitennavigation aufgerufen, wenn die Seite erscheint.</summary>
    public void Activate()
    {
        UpdateSummary();
        ExamList.Focus();
    }

    private CalendarEvent? Selected => ExamList.SelectedItem as CalendarEvent;

    // ==== Anlegen, Löschen, Anzeigen ====

    private void NewExam_Click(object sender, RoutedEventArgs e)
    {
        var exam = store.Add(CalendarEventKind.Pruefung);
        exam.Subject = "Neues Fach";

        ExamList.SelectedItem = exam;
        ExamList.ScrollIntoView(exam);

        status.SetStatus("Prüfung angelegt - sie steht schon im Kalender.", StatusKind.Success);
    }

    private void DeleteExam_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } exam)
            return;

        if (MessageBox.Show(Window.GetWindow(this)!, $"„{exam.DisplayTitle}“ löschen?", "Prüfung löschen",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        store.Remove(exam);
        status.SetStatus("Prüfung gelöscht.", StatusKind.Info);
        UpdateSummary();
    }

    private void ShowInCalendar_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } exam)
            ShowInCalendarRequested?.Invoke(exam.Start.LocalDateTime);
    }

    // ==== Ausgeben ====

    private void ExportOne_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } exam)
            return;

        Export([exam], $"Pruefung {Sanitise(exam.DisplayTitle)}");
    }

    private void ExportAll_Click(object sender, RoutedEventArgs e)
    {
        var exams = store.Exams.ToList();

        if (exams.Count == 0)
        {
            status.SetStatus("Es ist keine Prüfung erfasst.", StatusKind.Error);
            return;
        }

        Export(exams, "Pruefungen");
    }

    private void Export(IReadOnlyCollection<CalendarEvent> exams, string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Als ICS-Datei speichern",
            Filter = "Kalenderdatei (*.ics)|*.ics",
            FileName = $"{suggestedName}.ics",
            AddExtension = true,
            DefaultExt = ".ics"
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        var entries = exams.Select(exam => new CalendarEntry
        {
            Kind = CalendarEntryKind.Pruefung,
            Title = exam.DisplayTitle,
            Subtitle = exam.Room,
            Description = exam.Notes,
            Start = exam.Start.LocalDateTime,
            End = exam.End,
            IsAllDay = exam.IsAllDay,
            Id = exam.Id
        });

        try
        {
            var count = IcsWriter.Write(dialog.FileName, entries);
            status.SetStatus(
                $"{count} {(count == 1 ? "Prüfung" : "Prüfungen")} nach {Path.GetFileName(dialog.FileName)} geschrieben.",
                StatusKind.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            status.SetStatus($"Speichern fehlgeschlagen: {ex.Message}", StatusKind.Error);
        }
    }

    /// <summary>Entfernt Zeichen, die in Dateinamen nicht erlaubt sind.</summary>
    private static string Sanitise(string value) =>
        string.Concat(value.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));

    // ==== Eingabefelder mit eigener Prüfung ====

    private void DateBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && !DateTime.TryParse(box.Text,
                CultureInfo.GetCultureInfo("de-CH"), DateTimeStyles.None, out _))
            status.SetStatus($"„{box.Text}“ ist kein Datum - erwartet wird tt.mm.jjjj.", StatusKind.Error);
    }

    private void TimeBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && !TimeSpan.TryParse(box.Text,
                CultureInfo.GetCultureInfo("de-CH"), out _))
            status.SetStatus($"„{box.Text}“ ist keine Uhrzeit - erwartet wird hh:mm.", StatusKind.Error);
    }

    private void DurationBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && !TimeText.TryParse(box.Text, out _))
            status.SetStatus(
                $"„{box.Text}“ ist keine Zeitangabe - erlaubt sind etwa 45, 1:30 oder 1,5h.",
                StatusKind.Error);
    }

    // ==== Oberfläche ====

    private void ExamList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DetailScroller.ScrollToTop();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var exams = store.Exams.OrderBy(exam => exam.Start).ToList();
        var next = exams.FirstOrDefault(exam => exam.Start.LocalDateTime >= DateTime.Now);

        SummaryText.Text = exams.Count switch
        {
            0 => "Keine Prüfungen",
            1 => "1 Prüfung" + NextText(next),
            _ => $"{exams.Count} Prüfungen" + NextText(next)
        };

        var hasSelection = Selected is not null;

        DeleteExamButton.IsEnabled = hasSelection;
        ExportOneButton.IsEnabled = hasSelection;
        DetailScroller.Visibility = hasSelection ? Visibility.Visible : Visibility.Hidden;
        NoSelectionText.Visibility = hasSelection ? Visibility.Collapsed : Visibility.Visible;
        EmptyListText.Visibility = store.Exams.Any() ? Visibility.Collapsed : Visibility.Visible;

        static string NextText(CalendarEvent? next) =>
            next is null ? " · keine bevorstehende" : $" · als Nächstes {next.DisplayTitle} am {next.Start.LocalDateTime:dd.MM.yyyy}";
    }
}
