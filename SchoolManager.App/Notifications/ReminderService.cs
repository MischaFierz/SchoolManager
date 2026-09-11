using SchoolManager.App.Data;

namespace SchoolManager.App.Notifications;

/// <summary>
/// Sammelt alles ein, was noch offen ist: überfällige und in den nächsten Tagen
/// fällige Aufträge, Aufgaben, Hausaufgaben, Prüfungen, Termine und To-Dos.
///
/// Die Aufträge, Hausaufgaben und Termine kommen aus den laufenden Speichern
/// des Fensters; die To-Dos liest der Dienst selbst von der Festplatte, weil
/// die To-Do-Seite ihre Liste ohnehin nach jeder Änderung sofort schreibt und
/// der Dienst damit auch dann etwas findet, wenn nie jemand die Seite geöffnet
/// hat.
/// </summary>
public sealed class ReminderService(WorkStore work, HomeworkStore homework, EventStore events)
{
    private const string TodoFile = "todos.json";

    /// <summary>
    /// Alles Offene bis in <paramref name="leadDays"/> Tagen, das Überfällige
    /// zuerst und danach nach Termin sortiert.
    /// </summary>
    public IReadOnlyList<Reminder> Collect(int leadDays)
    {
        var until = DateTime.Today.AddDays(Math.Max(0, leadDays));

        var found = new List<Reminder>();

        found.AddRange(WorkReminders(until));
        found.AddRange(HomeworkReminders(until));
        found.AddRange(EventReminders(until));
        found.AddRange(TodoReminders(until));

        return found
            .OrderBy(item => item.Due ?? DateTimeOffset.MaxValue)
            .ThenBy(item => item.Kind)
            .ToList();
    }

    /// <summary>Aufträge und Aufgaben mit Enddatum; Leistungsdetails haben keines.</summary>
    private IEnumerable<Reminder> WorkReminders(DateTime until) =>
        work.AllNodes()
            .Where(node => node.SupportsDueDate && !node.IsDone)
            .Where(node => IsDue(node.DueDate, until))
            .Select(node => new Reminder(node.LevelName, node.DisplayTitle, node.DueDate));

    private IEnumerable<Reminder> HomeworkReminders(DateTime until) =>
        homework.Items
            .Where(item => !item.IsDone && IsDue(item.DueDate, until))
            .Select(item => new Reminder("Hausaufgabe", WithSubject(item.Subject, item.DisplayTitle), item.DueDate));

    /// <summary>
    /// Prüfungen und Termine, die bevorstehen. Vergangene bleiben aussen vor -
    /// eine Prüfung von letzter Woche ist nicht "offen", sie ist vorbei.
    /// </summary>
    private IEnumerable<Reminder> EventReminders(DateTime until) =>
        events.Items
            .Where(item => item.Start.LocalDateTime.Date >= DateTime.Today
                           && item.Start.LocalDateTime.Date <= until.Date)
            .Select(item => new Reminder(item.KindLabel, item.DisplayTitle, item.Start));

    private static IEnumerable<Reminder> TodoReminders(DateTime until) =>
        LocalStore.Load<TodoItem>(TodoFile)
            .Where(item => !item.IsDone && IsDue(item.DueDate, until))
            .Select(item => new Reminder("To-Do", WithSubject(item.Subject, item.Text), item.DueDate));

    /// <summary>Fällig heisst: Termin gesetzt und nicht später als der Stichtag.</summary>
    private static bool IsDue(DateTimeOffset? due, DateTime until) =>
        due is { } date && date.LocalDateTime.Date <= until.Date;

    private static string WithSubject(string subject, string title) =>
        string.IsNullOrWhiteSpace(subject) ? title : $"{subject}: {title}";
}
