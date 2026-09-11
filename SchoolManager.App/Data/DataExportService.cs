using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SchoolManager.App.Data;

/// <summary>Exportiert die Konfiguration oder alle Daten aus dem Benutzerprofil als Sicherung.</summary>
public static class DataExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Schreibt Konto-Art, Server und Absender ohne Passwort als JSON-Datei.</summary>
    public static void ExportConfig(string path)
    {
        if (!File.Exists(SettingsStore.FilePath))
            throw new FileNotFoundException("Es sind noch keine Einstellungen gespeichert.");

        var document = JsonNode.Parse(File.ReadAllText(SettingsStore.FilePath))?.AsObject()
                       ?? throw new InvalidDataException("Die Einstellungsdatei ist beschädigt.");

        // Das Passwort ist mit DPAPI an dieses Windows-Konto gebunden und wäre
        // anderswo ohnehin nicht lesbar - es gehört nicht in einen Export.
        document.Remove("ProtectedPassword");

        File.WriteAllText(path, document.ToJsonString(JsonOptions));
    }

    /// <summary>Packt den gesamten Datenordner (%APPDATA%\SchoolManager) in eine ZIP-Datei.</summary>
    public static void ExportAll(string path)
    {
        if (!Directory.Exists(LocalStore.Folder))
            throw new DirectoryNotFoundException("Es sind noch keine Daten vorhanden.");

        if (File.Exists(path))
            File.Delete(path);

        ZipFile.CreateFromDirectory(LocalStore.Folder, path, CompressionLevel.Optimal, includeBaseDirectory: false);
    }
}
