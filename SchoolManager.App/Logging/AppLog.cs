using System.IO;
using SchoolManager.App.Data;

namespace SchoolManager.App.Logging;

/// <summary>Eine aufgezeichnete Fehlermeldung.</summary>
/// <param name="Time">Wann sie auftrat.</param>
/// <param name="Source">Woher sie kam, etwa "Oberfläche" oder "Abgestürzt".</param>
/// <param name="Message">Der Wortlaut.</param>
public sealed record LogEntry(DateTimeOffset Time, string Source, string Message)
{
    public string TimeText => Time.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");

    /// <summary>Kopfzeile im Protokoll: Zeitpunkt und Herkunft.</summary>
    public string Header => $"{TimeText} · {Source}";

    /// <summary>Eine Zeile, wie sie in der Datei steht.</summary>
    public string Line => $"{TimeText}\t{Source}\t{Message}";
}

/// <summary>
/// Das Fehlerprotokoll der Anwendung.
///
/// Hier läuft alles auf, was schiefgegangen ist: jede Fehlermeldung der
/// Fussleiste und jeder Absturz. Angesehen wird es im Entwicklermodus unter
/// „Protokoll"; zusätzlich steht alles in einer Datei im Datenordner, damit
/// auch das nachlesbar bleibt, was die Anwendung nicht überlebt hat.
/// </summary>
public static class AppLog
{
    /// <summary>So viele Einträge bleiben im Speicher sichtbar.</summary>
    private const int MaxEntries = 300;

    /// <summary>Ab dieser Grösse fängt die Datei von vorne an.</summary>
    private const long MaxFileBytes = 512 * 1024;

    private static readonly object Lock = new();
    private static readonly List<LogEntry> Recorded = [];

    /// <summary>Die Protokolldatei im Datenordner.</summary>
    public static string FilePath => LocalStore.PathFor("fehler.log");

    /// <summary>Meldet jeden neuen Eintrag, damit die Oberfläche nachzieht.</summary>
    public static event Action? Changed;

    /// <summary>Das Aufgezeichnete, das Neueste zuoberst.</summary>
    public static IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (Lock)
                return Recorded.AsEnumerable().Reverse().ToList();
        }
    }

    public static int Count
    {
        get
        {
            lock (Lock)
                return Recorded.Count;
        }
    }

    /// <summary>Zeichnet eine Fehlermeldung auf.</summary>
    public static void Error(string message, string source = "Anwendung")
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var entry = new LogEntry(DateTimeOffset.Now, source, message.Trim());

        lock (Lock)
        {
            Recorded.Add(entry);

            // Nicht endlos mitwachsen; das Älteste fällt hinten heraus.
            if (Recorded.Count > MaxEntries)
                Recorded.RemoveRange(0, Recorded.Count - MaxEntries);
        }

        Append(entry);
        Changed?.Invoke();
    }

    /// <summary>Zeichnet einen Absturz mit Art und Ort auf.</summary>
    public static void Crash(Exception exception, string source) =>
        Error($"{exception.GetType().Name}: {exception.Message}", source);

    /// <summary>Leert Protokoll und Datei.</summary>
    public static void Clear()
    {
        lock (Lock)
            Recorded.Clear();

        try
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Bleibt die Datei liegen, ist das kein Grund zur Aufregung.
        }

        Changed?.Invoke();
    }

    /// <summary>Alles als Text, zum Kopieren oder Verschicken.</summary>
    public static string AsText() => string.Join(Environment.NewLine, Entries.Select(entry => entry.Line));

    /// <summary>
    /// Hängt den Eintrag an die Datei. Das darf nie selbst einen Fehler
    /// auslösen - ein Protokoll, das die Anwendung zum Absturz bringt, wäre
    /// schlechter als gar keines.
    /// </summary>
    private static void Append(LogEntry entry)
    {
        try
        {
            Directory.CreateDirectory(LocalStore.Folder);

            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxFileBytes)
                File.Delete(FilePath);

            File.AppendAllText(FilePath, entry.Line + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Dann steht es eben nur im Speicher.
        }
    }
}
