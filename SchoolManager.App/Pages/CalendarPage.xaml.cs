using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using SchoolManager.App.Data;
using SchoolManager.App.Dialogs;

namespace SchoolManager.App.Pages;

/// <summary>
/// Der Kalender: Lektionen aus den Stundenplan-Dateien, Prüfungen, Termine,
/// Hausaufgaben und die Enddaten von Aufträgen und Aufgaben in einer
/// Wochenansicht. Alles lässt sich als ICS-Datei ausgeben.
/// </summary>
public partial class CalendarPage : UserControl
{
    /// <summary>Wie viele Bildpunkte eine Minute im Zeitraster hoch ist.</summary>
    internal const double MinuteHeight = 1.2;

    /// <summary>Ohne Einträge zeigt das Raster diesen Ausschnitt des Tages.</summary>
    private const double DefaultDayStart = 7 * 60;

    private const double DefaultDayEnd = 18 * 60;

    private readonly TimetableStore timetable;
    private readonly LessonPlanStore plan;
    private readonly EventStore events;
    private readonly TeacherStore teachers;
    private readonly CalendarFeed feed;
    private readonly IStatusSink status;
    private readonly ObservableCollection<DayColumn> days = [];

    private DateTime weekStart = IcsParser.StartOfWeek(DateTime.Today);

    public CalendarPage(
        TimetableStore timetable,
        LessonPlanStore plan,
        EventStore events,
        HomeworkStore homework,
        WorkStore work,
        TeacherStore teachers,
        CalendarFeed feed,
        IStatusSink status)
    {
        this.timetable = timetable;
        this.plan = plan;
        this.events = events;
        this.teachers = teachers;
        this.feed = feed;
        this.status = status;

        InitializeComponent();

        // Kopfzeile, ganztägige Einträge und Raster zeigen dieselben Tage.
        WeekDays.ItemsSource = days;
        DayHeads.ItemsSource = days;
        AllDayRow.ItemsSource = days;
        SourceList.ItemsSource = timetable.Sources;

        // Lektionen, Prüfungen, Hausaufgaben und Abgaben können sich anderswo ändern.
        plan.Changed += ShowWeek;
        events.Changed += ShowWeek;
        homework.Changed += ShowWeek;
        work.Changed += ShowWeek;
        teachers.Changed += ShowWeek;

        ShowWeek();
    }

    /// <summary>Wird von der Seitennavigation aufgerufen, wenn die Seite erscheint.</summary>
    public void Activate() => ShowWeek();

    /// <summary>Springt in die Woche eines Datums - etwa von einer Prüfung aus.</summary>
    public void ShowWeekOf(DateTime date)
    {
        weekStart = IcsParser.StartOfWeek(date);
        ShowWeek();
    }

    /// <summary>
    /// Zeigt das Verlaufsband am unteren Rand der ganztägigen Einträge nur, wenn dort
    /// tatsächlich noch etwas abgeschnitten ist - sonst würde es einer vollständig
    /// sichtbaren letzten Karte grundlos den unteren Rand ausblenden.
    /// </summary>
    private void AllDayScroll_ScrollChanged(object sender, ScrollChangedEventArgs e) =>
        AllDayFade.Visibility = e.ExtentHeight > e.ViewportHeight ? Visibility.Visible : Visibility.Collapsed;

    // ==== Woche wählen ====

    private void PreviousWeek_Click(object sender, RoutedEventArgs e)
    {
        weekStart = weekStart.AddDays(-7);
        ShowWeek();
    }

    private void NextWeek_Click(object sender, RoutedEventArgs e)
    {
        weekStart = weekStart.AddDays(7);
        ShowWeek();
    }

    private void Today_Click(object sender, RoutedEventArgs e)
    {
        weekStart = IcsParser.StartOfWeek(DateTime.Today);
        ShowWeek();
    }

    // ==== Lektionen bearbeiten ====

    /// <summary>Legt eine Lektion an, die jede Woche an diesem Wochentag gilt.</summary>
    private void AddLesson_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DayColumn column })
            return;

        var draft = new TimetableEntry
        {
            IsWeekly = true,
            Day = column.Date.DayOfWeek,
            Date = new DateTimeOffset(column.Date),
            Subject = ""
        };

        try
        {
            var dialog = new LessonDialog(draft, teachers,
                $"Neue Lektion am {column.DayName}",
                "Die Lektion gehört zum eigenen Stundenplan und lässt sich jederzeit ändern.")
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() != true)
                return;

            var created = dialog.Result;
            plan.Entries.Add(created);
            plan.AssignTeacher(created.Subject, created.TeacherId);

            ShowWeek();
            status.SetStatus($"Lektion „{created.DisplayTitle}“ eingetragen: {created.WhenText}.",
                StatusKind.Success);
        }
        catch (Exception ex)
        {
            status.SetStatus($"Der Dialog konnte nicht geöffnet werden: {ex.Message}", StatusKind.Error);
        }

    }

    /// <summary>
    /// Der Eintrag hinter einer Karte. Im Raster steckt er in einem PlacedEntry,
    /// über dem Raster steht er direkt.
    /// </summary>
    private static CalendarEntry? EntryOf(object sender) => sender switch
    {
        FrameworkElement { DataContext: PlacedEntry placed } => placed.Entry,
        FrameworkElement { DataContext: CalendarEntry entry } => entry,
        _ => null
    };

    private void EditEntry_Click(object sender, RoutedEventArgs e)
    {
        if (EntryOf(sender) is not { } entry)
            return;

        switch (entry.Kind)
        {
            case CalendarEntryKind.Lektion:
                EditLesson(entry);
                break;

            case CalendarEntryKind.Pruefung or CalendarEntryKind.Termin:
                status.SetStatus("Prüfungen und Termine werden auf der Seite Prüfungen bearbeitet.",
                    StatusKind.Info);
                break;

            case CalendarEntryKind.Hausaufgabe:
                status.SetStatus("Hausaufgaben werden auf der Seite Hausaufgaben bearbeitet.", StatusKind.Info);
                break;

            default:
                status.SetStatus("Abgabetermine stehen beim Auftrag bzw. bei der Aufgabe.", StatusKind.Info);
                break;
        }
    }

    /// <summary>
    /// Eigene Lektionen werden direkt geändert. Eine Lektion aus einer
    /// eingelesenen Datei wird in den eigenen Stundenplan übernommen und die
    /// Vorlage aus der Datei danach ausgeblendet.
    /// </summary>
    private void EditLesson(CalendarEntry entry)
    {
        var fromFile = entry.Lesson is null;

        var draft = entry.Lesson?.Clone() ?? new TimetableEntry
        {
            IsWeekly = true,
            Day = entry.Start.DayOfWeek,
            Date = new DateTimeOffset(entry.Start.Date),
            StartMinutes = (int)entry.Start.TimeOfDay.TotalMinutes,
            DurationMinutes = Math.Max(5, (int)(entry.End - entry.Start).TotalMinutes),
            Subject = entry.Title,
            // Der Untertitel ist "Raum · Lehrkraft"; hier zählt nur der Raum.
            Room = entry.Subtitle.Split('·')[0].Trim(),
            Note = entry.Description
        };

        // Ist dem Fach schon eine Lehrkraft zugeordnet, ist sie vorausgewählt.
        draft.TeacherId ??= plan.TeacherIdFor(draft.Subject);

        var dialog = new LessonDialog(draft, teachers,
            fromFile ? $"Lektion „{entry.Title}“ übernehmen" : $"Lektion „{entry.Title}“ bearbeiten",
            fromFile
                ? "Die Lektion stammt aus einer eingelesenen Datei. Gespeichert wird sie als eigene Lektion; die aus der Datei wird an dieser Stelle ausgeblendet."
                : "Änderungen gelten ab sofort für alle Wochen.")
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true)
            return;

        var result = dialog.Result;

        if (fromFile)
        {
            plan.Entries.Add(result);
            plan.Hide(entry.SlotKey);
        }
        else
        {
            entry.Lesson!.CopyFrom(result);
        }

        plan.AssignTeacher(result.Subject, result.TeacherId);

        ShowWeek();
        status.SetStatus($"Lektion „{result.DisplayTitle}“ gespeichert: {result.WhenText}.", StatusKind.Success);
    }

    private void RemoveEntry_Click(object sender, RoutedEventArgs e)
    {
        if (EntryOf(sender) is not { } entry)
            return;

        if (entry.Kind != CalendarEntryKind.Lektion)
        {
            status.SetStatus("Nur Lektionen lassen sich hier entfernen.", StatusKind.Info);
            return;
        }

        if (entry.Lesson is { } lesson)
        {
            if (MessageBox.Show(Window.GetWindow(this)!,
                    $"Lektion „{lesson.DisplayTitle}“ ({lesson.WhenText}) löschen?", "Lektion löschen",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            plan.Remove(lesson);
            status.SetStatus("Lektion gelöscht.", StatusKind.Info);
        }
        else
        {
            plan.Hide(entry.SlotKey);
            status.SetStatus(
                $"„{entry.Title}“ am {entry.Start:dddd} {entry.Start:HH:mm} wird nicht mehr angezeigt.",
                StatusKind.Info);
        }

        ShowWeek();
    }

    private void RestoreHidden_Click(object sender, RoutedEventArgs e)
    {
        plan.ShowAll();
        ShowWeek();
        status.SetStatus("Ausgeblendete Lektionen werden wieder angezeigt.", StatusKind.Success);
    }

    // ==== Ausgeben ====

    private void ExportWeek_Click(object sender, RoutedEventArgs e)
    {
        var entries = feed.Between(weekStart, weekStart.AddDays(7));

        if (entries.Count == 0)
        {
            status.SetStatus("In dieser Woche steht nichts im Kalender.", StatusKind.Error);
            return;
        }

        Export(entries, $"Kalender KW{WeekNumber(weekStart)} {weekStart:yyyy}");
    }

    private void ExportAll_Click(object sender, RoutedEventArgs e)
    {
        // Ein Jahr zurück und ein Jahr voraus deckt Schuljahre ab.
        var entries = feed.Between(DateTime.Today.AddYears(-1), DateTime.Today.AddYears(1));

        if (entries.Count == 0)
        {
            status.SetStatus("Es gibt noch keine Kalendereinträge.", StatusKind.Error);
            return;
        }

        Export(entries, "Kalender School Manager");
    }

    private void ExportEntry_Click(object sender, RoutedEventArgs e)
    {
        if (EntryOf(sender) is not { } entry)
            return;

        Export([entry], $"Termin {Sanitise(entry.Title)}");
    }

    private void Export(IReadOnlyCollection<CalendarEntry> entries, string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Als ICS-Datei speichern",
            Filter = "Kalenderdatei (*.ics)|*.ics",
            FileName = $"{Sanitise(suggestedName)}.ics",
            AddExtension = true,
            DefaultExt = ".ics"
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        try
        {
            var count = IcsWriter.Write(dialog.FileName, entries);
            status.SetStatus(
                $"{count} {(count == 1 ? "Eintrag" : "Einträge")} nach {Path.GetFileName(dialog.FileName)} geschrieben.",
                StatusKind.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            status.SetStatus($"Speichern fehlgeschlagen: {ex.Message}", StatusKind.Error);
        }
    }

    private static string Sanitise(string value) =>
        string.Concat(value.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));

    // ==== Einlesen ====

    private void ImportSource_Click(object sender, RoutedEventArgs e)
    {
        if (AskForIcs("Stundenplan-Datei einlesen") is { } files)
            ImportAsSource(files);
    }

    private void ImportEvents_Click(object sender, RoutedEventArgs e)
    {
        if (AskForIcs("Termine importieren") is { } files)
            ImportAsEvents(files);
    }

    private string[]? AskForIcs(string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "Kalenderdateien (*.ics)|*.ics|Alle Dateien (*.*)|*.*",
            Multiselect = true
        };

        return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FileNames : null;
    }

    private void Page_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Page_Drop(object sender, DragEventArgs e)
    {
        // Gezogene Dateien werden als Stundenplan-Quelle aufgenommen.
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            ImportAsSource(files);
    }

    private void ImportAsSource(IEnumerable<string> paths)
    {
        var imported = 0;

        foreach (var path in paths)
        {
            try
            {
                var source = timetable.Import(path);
                imported++;
                status.SetStatus($"„{source.DisplayName}“ eingelesen: {source.EventCount} Termine.",
                    StatusKind.Success);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException
                                           or UnauthorizedAccessException)
            {
                status.SetStatus($"{Path.GetFileName(path)} konnte nicht eingelesen werden: {ex.Message}",
                    StatusKind.Error);
            }
        }

        if (imported > 0)
            ShowWeek();
    }

    /// <summary>
    /// Übernimmt einzelne Termine aus einer ICS-Datei als eigene Einträge.
    /// Wiederkehrende Termine gehören als Stundenplan-Quelle hinein und werden
    /// hier übersprungen.
    /// </summary>
    private void ImportAsEvents(IEnumerable<string> paths)
    {
        var added = 0;
        var skipped = 0;

        foreach (var path in paths)
        {
            try
            {
                var parsed = IcsParser.Parse(File.ReadAllText(path));
                var single = parsed.Where(item => item.Frequency == IcsFrequency.None).ToList();

                skipped += parsed.Count - single.Count;

                var lessons = IcsParser.Expand(single, DateTime.Today.AddYears(-5),
                    DateTime.Today.AddYears(5), Path.GetFileNameWithoutExtension(path));

                added += events.ImportLessons(lessons, CalendarEventKind.Termin);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                status.SetStatus($"{Path.GetFileName(path)} konnte nicht gelesen werden: {ex.Message}",
                    StatusKind.Error);
                return;
            }
        }

        var message = added switch
        {
            0 => "Es wurde kein neuer Termin gefunden.",
            1 => "1 Termin übernommen.",
            _ => $"{added} Termine übernommen."
        };

        if (skipped > 0)
            message += $" {skipped} wiederkehrende Termine übersprungen - die bitte als Stundenplan-Datei einlesen.";

        status.SetStatus(message, added > 0 ? StatusKind.Success : StatusKind.Info);
        ShowWeek();
    }

    private void RemoveSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TimetableSource source })
            return;

        if (MessageBox.Show(Window.GetWindow(this)!,
                $"„{source.DisplayName}“ aus dem Kalender entfernen?", "Quelle entfernen",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        timetable.Remove(source);
        ShowWeek();
        status.SetStatus("Quelle entfernt.", StatusKind.Info);
    }

    // ==== Anzeige ====

    private void ShowWeek()
    {
        var weekEnd = weekStart.AddDays(7);
        var entries = feed.Between(weekStart, weekEnd);

        // Der gezeigte Ausschnitt des Tages richtet sich nach der Woche: er
        // reicht mindestens von 07:00 bis 18:00 und wird bei früheren oder
        // späteren Einträgen auf die volle Stunde erweitert.
        var (dayStart, dayEnd) = DayRange(entries);
        var hours = HourRows(dayStart, dayEnd);

        HourAxis.ItemsSource = hours;

        days.Clear();

        for (var offset = 0; offset < 5; offset++)
        {
            var date = weekStart.AddDays(offset);
            days.Add(new DayColumn(date, entries.Where(entry => entry.Start.Date == date).ToList(),
                dayStart, dayEnd, hours));
        }

        // Die Zeile für ganztägige Einträge erscheint nur, wenn es welche gibt.
        var anyAllDay = days.Any(day => day.AllDay.Count > 0);

        AllDayScroll.Visibility = anyAllDay ? Visibility.Visible : Visibility.Collapsed;
        AllDayLabel.Visibility = anyAllDay ? Visibility.Visible : Visibility.Collapsed;

        var weekend = entries.Where(entry => entry.Start.Date >= weekStart.AddDays(5)).ToList();

        WeekendList.ItemsSource = weekend;
        WeekendPanel.Visibility = weekend.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        WeekText.Text = $"KW {WeekNumber(weekStart)} · {weekStart:dd.MM.} – {weekStart.AddDays(6):dd.MM.yyyy}";

        var minutes = entries.Where(entry => entry.IsLesson)
            .Sum(entry => (entry.End - entry.Start).TotalMinutes);

        var counts = entries.GroupBy(entry => entry.Kind)
            .OrderBy(group => group.Key)
            .Select(group => $"{group.Count()} {Label(group.Key, group.Count())}")
            .ToList();

        SummaryText.Text = entries.Count == 0
            ? "Diese Woche steht nichts im Kalender"
            : $"{string.Join(" · ", counts)} · {TimeText.Format((int)minutes)} h Unterricht" + NextText(entries);

        NoSourceText.Visibility = timetable.Sources.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RestoreHiddenButton.Visibility = plan.HiddenCount > 0 ? Visibility.Visible : Visibility.Collapsed;

        static string Label(CalendarEntryKind kind, int count) => (kind, count) switch
        {
            (CalendarEntryKind.Lektion, 1) => "Lektion",
            (CalendarEntryKind.Lektion, _) => "Lektionen",
            (CalendarEntryKind.Pruefung, 1) => "Prüfung",
            (CalendarEntryKind.Pruefung, _) => "Prüfungen",
            (CalendarEntryKind.Termin, 1) => "Termin",
            (CalendarEntryKind.Termin, _) => "Termine",
            (CalendarEntryKind.Hausaufgabe, 1) => "Hausaufgabe",
            (CalendarEntryKind.Hausaufgabe, _) => "Hausaufgaben",
            (_, 1) => "Abgabe",
            _ => "Abgaben"
        };
    }

    private static string NextText(IEnumerable<CalendarEntry> entries)
    {
        var next = entries.FirstOrDefault(entry => !entry.IsAllDay && entry.Start >= DateTime.Now);

        if (next is null)
            return "";

        var when = next.Start.Date == DateTime.Today
            ? $"heute {next.Start:HH:mm}"
            : $"{next.Start:ddd dd.MM.} {next.Start:HH:mm}";

        return $" · als Nächstes {next.Title} ({when})";
    }

    /// <summary>
    /// Der Ausschnitt des Tages, den das Raster zeigt - immer volle Stunden und
    /// weit genug, damit jeder Eintrag der Woche hineinpasst.
    /// </summary>
    private static (double Start, double End) DayRange(IEnumerable<CalendarEntry> entries)
    {
        var start = DefaultDayStart;
        var end = DefaultDayEnd;

        foreach (var entry in entries.Where(entry => !entry.IsAllDay))
        {
            var from = entry.Start.TimeOfDay.TotalMinutes;

            // Ein Eintrag über Mitternacht hinaus endet für die Anzeige um 24:00.
            var to = entry.End.Date > entry.Start.Date
                ? 24 * 60
                : entry.End.TimeOfDay.TotalMinutes;

            start = Math.Min(start, Math.Floor(from / 60) * 60);
            end = Math.Max(end, Math.Ceiling(to / 60) * 60);
        }

        return (start, Math.Max(end, start + 60));
    }

    /// <summary>Die Stundenzeilen der Achse und der Linien im Raster.</summary>
    private static List<HourRow> HourRows(double start, double end) =>
        Enumerable.Range(0, (int)((end - start) / 60))
            .Select(step => new HourRow(
                TimeSpan.FromMinutes(start + step * 60).ToString(@"hh\:mm"),
                60 * MinuteHeight))
            .ToList();

    /// <summary>
    /// Verteilt die Einträge eines Tages auf Spalten: Was sich zeitlich
    /// überschneidet, steht nebeneinander. Einträge, die sich nicht berühren,
    /// beginnen wieder bei der ersten Spalte und nutzen die volle Breite.
    /// </summary>
    private static List<PlacedEntry> Place(IEnumerable<CalendarEntry> entries, double dayStart, double dayEnd)
    {
        var placed = entries
            .Select(entry => new PlacedEntry(entry,
                Math.Clamp(entry.Start.TimeOfDay.TotalMinutes, dayStart, dayEnd),
                Math.Clamp(
                    entry.End.Date > entry.Start.Date ? 24 * 60 : entry.End.TimeOfDay.TotalMinutes,
                    dayStart, dayEnd)))
            .OrderBy(item => item.StartMinutes)
            .ThenByDescending(item => item.EndMinutes)
            .ToList();

        // Eine Gruppe sind alle Einträge, die über Überschneidungen zusammenhängen.
        var group = new List<PlacedEntry>();
        var columnEnds = new List<double>();

        foreach (var item in placed)
        {
            if (group.Count > 0 && item.StartMinutes >= group.Max(other => other.EndMinutes))
                Close();

            var column = columnEnds.FindIndex(end => end <= item.StartMinutes);

            if (column < 0)
            {
                columnEnds.Add(item.EndMinutes);
                column = columnEnds.Count - 1;
            }
            else
            {
                columnEnds[column] = item.EndMinutes;
            }

            item.Column = column;
            group.Add(item);
        }

        Close();

        return placed;

        void Close()
        {
            foreach (var item in group)
                item.ColumnCount = Math.Max(1, columnEnds.Count);

            group.Clear();
            columnEnds.Clear();
        }
    }

    private static int WeekNumber(DateTime date) =>
        CultureInfo.GetCultureInfo("de-CH").Calendar
            .GetWeekOfYear(date, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);

    /// <summary>Eine Stundenzeile im Raster.</summary>
    public sealed class HourRow(string label, double height)
    {
        public string Label { get; } = label;
        public double Height { get; } = height;
    }

    /// <summary>
    /// Ein Eintrag mit seinem Platz im Raster: Beginn und Ende in Minuten ab
    /// Mitternacht sowie die Spalte, wenn mehrere gleichzeitig laufen.
    /// </summary>
    public sealed class PlacedEntry(CalendarEntry entry, double startMinutes, double endMinutes)
    {
        public CalendarEntry Entry { get; } = entry;
        public double StartMinutes { get; } = startMinutes;
        public double EndMinutes { get; } = endMinutes;

        /// <summary>Die wievielte von mehreren gleichzeitigen Karten, ab 0.</summary>
        public int Column { get; set; }

        /// <summary>Wie viele Karten sich hier die Breite teilen.</summary>
        public int ColumnCount { get; set; } = 1;

        /// <summary>Im Raster ist wenig Platz; der Tooltip zeigt alles.</summary>
        public string TooltipText => Entry.TooltipText;
    }

    /// <summary>Ein Tag in der Wochenansicht.</summary>
    public sealed class DayColumn
    {
        public DayColumn(DateTime date, List<CalendarEntry> entries,
            double dayStart, double dayEnd, List<HourRow> hours)
        {
            Date = date;
            Entries = entries;
            DayStart = dayStart;
            DayEnd = dayEnd;
            Hours = hours;

            AllDay = entries.Where(entry => entry.IsAllDay).ToList();
            Timed = Place(entries.Where(entry => !entry.IsAllDay), dayStart, dayEnd);

            var lessons = entries.Count(entry => entry.IsLesson);
            var minutes = entries.Where(entry => entry.IsLesson)
                .Sum(entry => (entry.End - entry.Start).TotalMinutes);

            TotalText = entries.Count switch
            {
                0 => "nichts eingetragen",
                _ when lessons == 0 => $"{entries.Count} {(entries.Count == 1 ? "Eintrag" : "Einträge")}",
                _ => $"{lessons} Lektionen · {TimeText.Format((int)minutes)} h"
            };
        }

        public DateTime Date { get; }
        public List<CalendarEntry> Entries { get; }
        public string TotalText { get; }

        /// <summary>Einträge ohne Uhrzeit; sie stehen über dem Raster.</summary>
        public List<CalendarEntry> AllDay { get; }

        /// <summary>Einträge mit Uhrzeit, fertig auf Spalten verteilt.</summary>
        public List<PlacedEntry> Timed { get; }

        public List<HourRow> Hours { get; }
        public double DayStart { get; }
        public double DayEnd { get; }
        public double MinuteHeight => CalendarPage.MinuteHeight;

        public string DayName => Date.ToString("dddd", CultureInfo.GetCultureInfo("de-CH"));
        public string DateText => Date.ToString("dd.MM.yyyy");
        public bool IsToday => Date == DateTime.Today;

        /// <summary>Der heutige Tag wird hervorgehoben.</summary>
        public Brush BorderBrush => IsToday
            ? (Brush)Application.Current.FindResource("AccentHover")
            : (Brush)Application.Current.FindResource("Line");

        public Brush TitleBrush => IsToday
            ? (Brush)Application.Current.FindResource("Text")
            : (Brush)Application.Current.FindResource("TextMuted");

        public Visibility EmptyVisibility => Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
