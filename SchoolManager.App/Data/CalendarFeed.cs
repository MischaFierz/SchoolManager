using System.Windows;
using System.Windows.Media;

namespace SchoolManager.App.Data;

/// <summary>Woher ein Kalendereintrag kommt.</summary>
public enum CalendarEntryKind
{
    Lektion,
    Pruefung,
    Termin,
    Hausaufgabe,
    Abgabe
}

/// <summary>
/// Ein Eintrag im Kalender - egal ob Lektion aus dem eigenen Stundenplan oder
/// einer ICS-Datei, selbst erfasste Prüfung, Termin, Hausaufgabe oder
/// Abgabetermin eines Auftrags.
/// </summary>
public sealed class CalendarEntry
{
    public required CalendarEntryKind Kind { get; init; }
    public required string Title { get; init; }
    public string Subtitle { get; init; } = "";
    public required DateTime Start { get; init; }
    public required DateTime End { get; init; }
    public bool IsAllDay { get; init; }
    public string Description { get; init; } = "";
    public string Id { get; init; } = Guid.NewGuid().ToString("n");

    /// <summary>Name der zugeordneten Lehrkraft, wenn es eine gibt.</summary>
    public string TeacherName { get; init; } = "";

    /// <summary>Die eigene Lektion dahinter - dann ist der Eintrag bearbeitbar.</summary>
    public TimetableEntry? Lesson { get; init; }

    /// <summary>Kennung der Lektion aus einer ICS-Datei, zum Ausblenden.</summary>
    public string SlotKey { get; init; } = "";

    /// <summary>Lektion aus einer eingelesenen Datei - nicht direkt bearbeitbar.</summary>
    public bool IsFromFile => Kind == CalendarEntryKind.Lektion && Lesson is null;

    public string KindLabel => Kind switch
    {
        CalendarEntryKind.Lektion => "Lektion",
        CalendarEntryKind.Pruefung => "Prüfung",
        CalendarEntryKind.Termin => "Termin",
        CalendarEntryKind.Hausaufgabe => "Hausaufgabe",
        _ => "Abgabe"
    };

    public string TimeText => IsAllDay ? "ganztägig" : $"{Start:HH:mm}–{End:HH:mm}";

    /// <summary>Nur Lektionen sind Unterricht; der Rest zählt nicht zur Unterrichtszeit.</summary>
    public bool IsLesson => Kind == CalendarEntryKind.Lektion;

    /// <summary>Prüfungen und dergleichen werden mit einer Marke hervorgehoben.</summary>
    public Visibility KindVisibility =>
        Kind == CalendarEntryKind.Lektion ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// Die gedämpfte Farbe, an der sich die Art des Eintrags auf einen Blick
    /// erkennen lässt - im Kalender der Streifen am linken Rand einer Karte.
    /// </summary>
    public Brush KindBrush => (Brush)Application.Current.FindResource(Kind switch
    {
        CalendarEntryKind.Pruefung => "KindPruefung",
        CalendarEntryKind.Termin => "KindTermin",
        CalendarEntryKind.Hausaufgabe => "KindHausaufgabe",
        CalendarEntryKind.Abgabe => "KindAbgabe",
        _ => "KindLektion"
    });

    /// <summary>Auf der Karte ist wenig Platz; der Tooltip zeigt alles.</summary>
    public string TooltipText
    {
        get
        {
            var lines = new List<string> { $"{KindLabel} · {TimeText}", Title };

            if (!string.IsNullOrWhiteSpace(Subtitle))
                lines.Add(Subtitle);

            if (!string.IsNullOrWhiteSpace(Description))
                lines.Add(Description);

            return string.Join(Environment.NewLine, lines);
        }
    }

    public override string ToString() => $"{KindLabel}: {Title}, {TimeText}";
}

/// <summary>
/// Führt alle Quellen zu einem Kalender zusammen: eigene Lektionen,
/// Stundenplan-Dateien, Prüfungen, Termine, Hausaufgaben und die Enddaten von
/// Aufträgen und Aufgaben.
/// </summary>
public sealed class CalendarFeed(
    TimetableStore timetable,
    LessonPlanStore plan,
    EventStore events,
    HomeworkStore homework,
    WorkStore work,
    TeacherStore teachers)
{
    public List<CalendarEntry> Between(DateTime from, DateTime to)
    {
        var entries = new List<CalendarEntry>();

        // Eigene Lektionen
        for (var date = from.Date; date < to.Date; date = date.AddDays(1))
        {
            foreach (var lesson in plan.Entries.Where(entry => entry.AppliesTo(date)))
            {
                var start = lesson.StartOn(date);
                var teacher = TeacherFor(lesson);

                entries.Add(new CalendarEntry
                {
                    Kind = CalendarEntryKind.Lektion,
                    Title = lesson.DisplayTitle,
                    Subtitle = Join(lesson.Room, teacher),
                    Description = lesson.Note,
                    Start = start,
                    End = start.AddMinutes(lesson.DurationMinutes),
                    TeacherName = teacher,
                    Lesson = lesson,
                    Id = lesson.Id
                });
            }
        }

        // Lektionen aus den eingelesenen ICS-Dateien
        foreach (var lesson in timetable.LessonsBetween(from, to))
        {
            var slot = LessonPlanStore.SlotKey(lesson.Start, lesson.Subject);

            if (plan.IsHidden(slot))
                continue;

            var teacher = TeacherNameById(plan.TeacherIdFor(lesson.Subject));

            entries.Add(new CalendarEntry
            {
                Kind = CalendarEntryKind.Lektion,
                Title = lesson.Subject,
                Subtitle = Join(lesson.Location, teacher),
                Description = lesson.Description,
                Start = lesson.Start,
                End = lesson.End,
                IsAllDay = lesson.IsAllDay,
                TeacherName = teacher,
                SlotKey = slot
            });
        }

        foreach (var item in events.Between(from, to))
            entries.Add(new CalendarEntry
            {
                Kind = item.Kind == CalendarEventKind.Pruefung
                    ? CalendarEntryKind.Pruefung
                    : CalendarEntryKind.Termin,
                Title = item.DisplayTitle,
                Subtitle = item.Room,
                Description = item.Notes,
                Start = item.Start.LocalDateTime,
                End = item.End,
                IsAllDay = item.IsAllDay,
                Id = item.Id
            });

        foreach (var item in homework.DueBetween(from, to))
        {
            var day = item.DueDate!.Value.LocalDateTime.Date;

            entries.Add(new CalendarEntry
            {
                Kind = CalendarEntryKind.Hausaufgabe,
                Title = item.DisplayTitle,
                Subtitle = item.IsDone
                    ? "erledigt"
                    : string.IsNullOrWhiteSpace(item.Subject) ? "offen" : item.Subject,
                Description = item.Notes,
                Start = day,
                End = day.AddDays(1),
                IsAllDay = true,
                Id = item.Id
            });
        }

        // Enddaten der Aufträge. Aufgaben stehen bewusst nicht im Kalender: sie
        // haben das Datum ihres Auftrags geerbt und würden ihn nur vervielfachen -
        // ein Auftrag mit fünf Aufgaben ergäbe sechs Abgaben am selben Tag. Die
        // Aufgaben stehen auf ihrer eigenen Seite und beim Auftrag.
        // Abgeschlossenes verschwindet ebenfalls: die Abgabe steht an, solange
        // sie offen ist, und ist danach erledigt statt fällig.
        foreach (var node in work.Orders.Where(order => !order.IsDone))
        {
            if (node.DueDate is not { } due)
                continue;

            var day = due.LocalDateTime.Date;

            if (day < from.Date || day >= to.Date)
                continue;

            entries.Add(new CalendarEntry
            {
                Kind = CalendarEntryKind.Abgabe,
                Title = $"{node.LevelName}: {node.DisplayTitle}",
                Subtitle = node.EstimateSummary,
                Description = node.Details,
                Start = day,
                End = day.AddDays(1),
                IsAllDay = true,
                Id = node.Id
            });
        }

        return entries
            .OrderBy(entry => entry.Start)
            .ThenByDescending(entry => entry.IsAllDay)
            .ThenBy(entry => entry.Title)
            .ToList();
    }

    /// <summary>Die Lektion, die zu diesem Zeitpunkt gerade läuft.</summary>
    public CalendarEntry? CurrentLesson(DateTime now) =>
        Between(now.Date, now.Date.AddDays(1))
            .FirstOrDefault(entry => entry.IsLesson && !entry.IsAllDay &&
                                     entry.Start <= now && entry.End > now);

    /// <summary>
    /// Die Lehrkraft der gerade laufenden Lektion - Vorlage für den
    /// Auftraggeber einer neuen Aufgabe.
    /// </summary>
    public CalendarEntry? CurrentLessonWithTeacher(DateTime now)
    {
        var lesson = CurrentLesson(now);

        return string.IsNullOrWhiteSpace(lesson?.TeacherName) ? null : lesson;
    }

    /// <summary>Lehrkraft einer eigenen Lektion: erst direkt, sonst über das Fach.</summary>
    private string TeacherFor(TimetableEntry lesson) =>
        TeacherNameById(lesson.TeacherId) is { Length: > 0 } direct
            ? direct
            : TeacherNameById(plan.TeacherIdFor(lesson.Subject));

    private string TeacherNameById(string? id) =>
        string.IsNullOrEmpty(id)
            ? ""
            : teachers.Items.FirstOrDefault(teacher => teacher.Id == id)?.DisplayName ?? "";

    private static string Join(params string[] parts) =>
        string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
}
