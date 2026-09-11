using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SchoolManager.App.Data;

/// <summary>Ordnet einem Fach eine Lehrkraft zu.</summary>
public sealed class SubjectTeacher
{
    public string Subject { get; set; } = "";
    public string TeacherId { get; set; } = "";

    public override string ToString() => $"{Subject} → {TeacherId}";
}

/// <summary>
/// Der von Hand gepflegte Teil des Stundenplans: eigene Lektionen, die
/// Zuordnung Fach → Lehrkraft und die aus den ICS-Dateien ausgeblendeten
/// Lektionen.
/// </summary>
public sealed class LessonPlanStore
{
    private const string LessonFile = "lektionen.json";
    private const string SubjectFile = "faecher.json";
    private const string HiddenFile = "ausgeblendet.json";

    private readonly HashSet<string> hidden;

    public LessonPlanStore()
    {
        Entries = new ObservableCollection<TimetableEntry>(LocalStore.Load<TimetableEntry>(LessonFile));

        foreach (var entry in Entries)
            entry.PropertyChanged += Entry_PropertyChanged;

        Entries.CollectionChanged += Entries_CollectionChanged;

        SubjectTeachers = LocalStore.Load<SubjectTeacher>(SubjectFile);
        hidden = new HashSet<string>(LocalStore.Load<string>(HiddenFile), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Selbst erfasste Lektionen.</summary>
    public ObservableCollection<TimetableEntry> Entries { get; }

    /// <summary>Zuordnung Fach → Lehrkraft, gilt auch für Lektionen aus ICS-Dateien.</summary>
    public List<SubjectTeacher> SubjectTeachers { get; }

    public event Action? Changed;

    public event Action<string>? SaveFailed;

    /// <summary>Legt eine Lektion an, die jede Woche an diesem Wochentag gilt.</summary>
    public TimetableEntry AddWeekly(DayOfWeek day, int startMinutes = 8 * 60 + 15)
    {
        var entry = new TimetableEntry
        {
            IsWeekly = true,
            Day = day,
            StartMinutes = startMinutes,
            Subject = "Neues Fach"
        };

        Entries.Add(entry);
        return entry;
    }

    public void Remove(TimetableEntry entry) => Entries.Remove(entry);

    // ==== Fach und Lehrkraft ====

    /// <summary>Welche Lehrkraft unterrichtet dieses Fach?</summary>
    public string? TeacherIdFor(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return null;

        return SubjectTeachers
            .FirstOrDefault(item => string.Equals(item.Subject, subject.Trim(), StringComparison.OrdinalIgnoreCase))
            ?.TeacherId;
    }

    /// <summary>
    /// Ordnet dem Fach eine Lehrkraft zu; damit gilt sie für alle Lektionen
    /// dieses Fachs - auch für die aus den ICS-Dateien.
    /// </summary>
    public void AssignTeacher(string subject, string? teacherId)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return;

        var existing = SubjectTeachers.FirstOrDefault(item =>
            string.Equals(item.Subject, subject.Trim(), StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(teacherId))
        {
            if (existing is not null)
                SubjectTeachers.Remove(existing);
        }
        else if (existing is null)
        {
            SubjectTeachers.Add(new SubjectTeacher { Subject = subject.Trim(), TeacherId = teacherId });
        }
        else
        {
            existing.TeacherId = teacherId;
        }

        SaveSubjects();
        Changed?.Invoke();
    }

    // ==== Ausgeblendete Lektionen aus ICS-Dateien ====

    /// <summary>
    /// Kennung einer Lektion aus einer Datei: Wochentag, Beginn und Fach. So
    /// bleibt das Ausblenden auch nach einem neuen Einlesen wirksam.
    /// </summary>
    public static string SlotKey(DateTime start, string subject) =>
        $"{(int)start.DayOfWeek}|{start:HHmm}|{subject.Trim().ToLowerInvariant()}";

    public bool IsHidden(string slotKey) => hidden.Contains(slotKey);

    public void Hide(string slotKey)
    {
        if (!hidden.Add(slotKey))
            return;

        SaveHidden();
        Changed?.Invoke();
    }

    public void Show(string slotKey)
    {
        if (!hidden.Remove(slotKey))
            return;

        SaveHidden();
        Changed?.Invoke();
    }

    public int HiddenCount => hidden.Count;

    /// <summary>Holt alle ausgeblendeten Lektionen zurück.</summary>
    public void ShowAll()
    {
        if (hidden.Count == 0)
            return;

        hidden.Clear();
        SaveHidden();
        Changed?.Invoke();
    }

    // ==== Speichern ====

    private void Entries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var entry in e.OldItems?.OfType<TimetableEntry>() ?? [])
            entry.PropertyChanged -= Entry_PropertyChanged;

        foreach (var entry in e.NewItems?.OfType<TimetableEntry>() ?? [])
            entry.PropertyChanged += Entry_PropertyChanged;

        SaveEntries();
        Changed?.Invoke();
    }

    private void Entry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        SaveEntries();
        Changed?.Invoke();
    }

    private void SaveEntries() => Report(LocalStore.TrySave(LessonFile, Entries));

    private void SaveSubjects() => Report(LocalStore.TrySave(SubjectFile, SubjectTeachers));

    private void SaveHidden() => Report(LocalStore.TrySave(HiddenFile, hidden));

    private void Report(string? error)
    {
        if (error is not null)
            SaveFailed?.Invoke(error);
    }
}
