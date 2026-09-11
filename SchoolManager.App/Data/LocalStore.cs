using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SchoolManager.App.Data;

/// <summary>
/// Legt Listen als JSON-Datei im Benutzerprofil ab
/// (<c>%APPDATA%\SchoolManager</c>) und liest sie wieder ein.
/// </summary>
public static class LocalStore
{
    public static string Folder { get; }

    static LocalStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        Folder = Path.Combine(appData, "SchoolManager");

        // Die App hiess früher EmailSender: vorhandene Daten mitnehmen,
        // damit nach der Umbenennung nichts verloren geht.
        var previous = Path.Combine(appData, "EmailSender");

        if (Directory.Exists(previous) && !Directory.Exists(Folder))
        {
            try
            {
                Directory.Move(previous, Folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Bleibt der alte Ordner liegen, startet die App eben leer.
            }
        }
    }

    // Leere Felder werden nicht geschrieben - so bleiben etwa die nur noch zum
    // Einlesen vorhandenen Alt-Bezeichnungen aus den Dateien draussen.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string PathFor(string fileName) => Path.Combine(Folder, fileName);

    public static List<T> Load<T>(string fileName)
    {
        var path = PathFor(fileName);

        if (!File.Exists(path))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path), JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Eine beschädigte Datei soll den Start nicht verhindern.
            return [];
        }
    }

    /// <summary>Schreibt die Liste; gibt die Fehlermeldung zurück, falls es nicht klappt.</summary>
    public static string? TrySave<T>(string fileName, IEnumerable<T> items)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(PathFor(fileName), JsonSerializer.Serialize(items, JsonOptions));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }
}
