using System.IO;
using System.IO.Compression;
using System.Text.Json;
using SchoolManager.Core;

namespace SchoolManager.App.Data;

/// <summary>Liest eine mit <see cref="DataExportService"/> erstellte Konfiguration oder Datensicherung wieder ein.</summary>
public static class DataImportService
{
    /// <summary>Liest eine exportierte Konfigurationsdatei; wirft, wenn die Datei kein gültiges JSON-Objekt ist.</summary>
    public static SmtpSettings ImportConfig(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Die Datei wurde nicht gefunden.", path);

        // Vorab prüfen, damit eine beschädigte oder falsche Datei nicht
        // stillschweigend leere Einstellungen liefert.
        try
        {
            JsonDocument.Parse(File.ReadAllText(path)).Dispose();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Die Datei ist keine gültige Konfigurationsdatei.", ex);
        }

        return SettingsStore.Load(path);
    }

    /// <summary>
    /// Entpackt eine mit <see cref="DataExportService.ExportAll"/> erzeugte ZIP-Datei in den Datenordner;
    /// bestehende Dateien werden überschrieben.
    /// </summary>
    public static void ImportAll(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Die Datei wurde nicht gefunden.", path);

        using var archive = ZipFile.OpenRead(path);

        if (archive.Entries.Count == 0)
            throw new InvalidDataException("Die ZIP-Datei enthält keine Daten.");

        Directory.CreateDirectory(LocalStore.Folder);

        var root = Path.GetFullPath(LocalStore.Folder + Path.DirectorySeparatorChar);

        foreach (var entry in archive.Entries)
        {
            // Verzeichniseinträge haben keinen Dateinamen.
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var destination = Path.GetFullPath(Path.Combine(LocalStore.Folder, entry.FullName));

            // Schutz gegen "Zip-Slip": Einträge dürfen nicht aus dem Datenordner herausführen.
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Die ZIP-Datei enthält ungültige Pfade.");

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }
    }
}
