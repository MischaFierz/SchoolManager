using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Serialization;

namespace SchoolManager.App.Data;

/// <summary>Eine eingelesene ICS-Datei.</summary>
public sealed class TimetableSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string DisplayName { get; set; } = "";
    public string OriginalPath { get; set; } = "";
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.Now;
    public int EventCount { get; set; }

    [JsonIgnore]
    public string Summary => $"{EventCount} Termine · eingelesen am {ImportedAt.ToLocalTime():dd.MM.yyyy HH:mm}";

    public override string ToString() => DisplayName;
}

/// <summary>
/// Verwaltet die eingelesenen ICS-Dateien: kopiert sie ins Benutzerprofil,
/// hält die Termine im Speicher und rechnet die Lektionen einer Woche aus.
/// </summary>
public sealed class TimetableStore
{
    private const string IndexFile = "stundenplan.json";

    private readonly Dictionary<string, List<IcsEvent>> parsed = [];

    public TimetableStore()
    {
        Folder = Path.Combine(LocalStore.Folder, "Stundenplan");
        Sources = new ObservableCollection<TimetableSource>(LocalStore.Load<TimetableSource>(IndexFile));

        foreach (var source in Sources.ToList())
            LoadEvents(source);
    }

    public string Folder { get; }

    public ObservableCollection<TimetableSource> Sources { get; }

    public int EventCount => parsed.Values.Sum(list => list.Count);

    public string FileFor(TimetableSource source) => Path.Combine(Folder, $"{source.Id}.ics");

    /// <summary>
    /// Liest eine ICS-Datei ein und legt eine Kopie im Benutzerprofil ab, damit
    /// der Stundenplan auch dann noch steht, wenn die Datei weg ist.
    /// </summary>
    public TimetableSource Import(string path)
    {
        var content = File.ReadAllText(path);
        var events = IcsParser.Parse(content);

        if (events.Count == 0)
            throw new InvalidOperationException("In dieser Datei stehen keine Termine (VEVENT).");

        var source = new TimetableSource
        {
            DisplayName = Path.GetFileNameWithoutExtension(path),
            OriginalPath = path,
            EventCount = events.Count
        };

        Directory.CreateDirectory(Folder);
        File.WriteAllText(FileFor(source), content);

        parsed[source.Id] = events;
        Sources.Add(source);
        SaveIndex();

        return source;
    }

    public void Remove(TimetableSource source)
    {
        parsed.Remove(source.Id);
        Sources.Remove(source);

        try
        {
            var file = FileFor(source);

            if (File.Exists(file))
                File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Die Datei bleibt liegen; im Stundenplan ist sie trotzdem weg.
        }

        SaveIndex();
    }

    /// <summary>Alle Lektionen im Zeitraum, nach Beginn sortiert.</summary>
    public List<Lesson> LessonsBetween(DateTime from, DateTime to)
    {
        var lessons = new List<Lesson>();

        foreach (var source in Sources)
        {
            if (!parsed.TryGetValue(source.Id, out var events))
                continue;

            lessons.AddRange(IcsParser.Expand(events, from, to, source.DisplayName));
        }

        return lessons.OrderBy(lesson => lesson.Start).ThenBy(lesson => lesson.Subject).ToList();
    }

    /// <summary>Die nächste Lektion ab jetzt, für den Hinweis in der Kopfzeile.</summary>
    public Lesson? NextLesson()
    {
        var now = DateTime.Now;

        return LessonsBetween(now, now.AddDays(14)).FirstOrDefault(lesson => !lesson.IsAllDay);
    }

    private void LoadEvents(TimetableSource source)
    {
        var file = FileFor(source);

        if (!File.Exists(file))
        {
            // Kopie fehlt: der Eintrag bringt nichts mehr.
            Sources.Remove(source);
            return;
        }

        try
        {
            parsed[source.Id] = IcsParser.Parse(File.ReadAllText(file));
        }
        catch (IOException)
        {
            parsed[source.Id] = [];
        }
    }

    private void SaveIndex() => LocalStore.TrySave(IndexFile, Sources);
}
