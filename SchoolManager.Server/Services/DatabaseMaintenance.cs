using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SchoolManager.Server.Data;

namespace SchoolManager.Server.Services;

/// <summary>Wo die Datenbank arbeitet und wohin sie gesichert wird.</summary>
/// <param name="Database">Die Arbeitsdatei.</param>
/// <param name="Backup">
/// Eine laufend nachgeführte Kopie, etwa auf einem dauerhaften Laufwerk; null, wenn keine gewünscht ist.
/// </param>
/// <param name="SnapshotFolder">Hier liegen die täglichen Sicherungen.</param>
public sealed record StorageLocations(string Database, string? Backup, string SnapshotFolder)
{
    /// <summary>
    /// Liest Storage:Database, Storage:Backup und Storage:Snapshots. Relative
    /// Pfade gelten ab dem Ordner des Servers.
    /// </summary>
    public static StorageLocations From(IConfiguration configuration, string contentRoot)
    {
        string Full(string path) => Path.GetFullPath(path, contentRoot);

        var database = Full(configuration["Storage:Database"] ?? Path.Combine("App_Data", "schoolmanager.db"));
        var backup = configuration["Storage:Backup"] is { Length: > 0 } b ? Full(b) : null;

        var snapshots = configuration["Storage:Snapshots"] is { Length: > 0 } s
            ? Full(s)
            : Path.Combine(Path.GetDirectoryName(backup ?? database)!, "Sicherungen");

        return new StorageLocations(database, backup, snapshots);
    }
}

/// <summary>
/// Hält die Datenbank in Ordnung, solange der Server läuft.
///
/// SQLite verträgt Netzlaufwerke schlecht - auf Azure liegt /home aber genau
/// auf einem. Deshalb arbeitet der Server mit einer Datei auf der lokalen
/// Platte und schreibt sie laufend auf das dauerhafte Laufwerk zurück: nach
/// jeder Änderung spätestens nach einer Minute und beim Beenden. Startet der
/// Server auf einer leeren Platte, holt er sich zuerst diese Kopie.
///
/// Dazu je Tag eine Sicherung, 14 Tage lang, und alle sechs Stunden werden
/// abgelaufene Anmeldungen weggeräumt.
/// </summary>
public sealed class DatabaseMaintenance(
    StorageLocations storage,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<DatabaseMaintenance> logger) : BackgroundService
{
    private const int SnapshotDays = 14;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(6);

    private DateTime lastBackup = DateTime.MinValue;
    private DateTimeOffset lastCleanup = DateTimeOffset.MinValue;

    /// <summary>
    /// Vor dem ersten Zugriff: Fehlt die Arbeitsdatei, aber es gibt eine Kopie,
    /// wird sie zurückgeholt. Muss laufen, bevor die Datenbank geöffnet wird.
    /// </summary>
    public static void RestoreIfMissing(StorageLocations storage, ILogger logger)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(storage.Database)!);

        if (File.Exists(storage.Database) || storage.Backup is null || !File.Exists(storage.Backup))
            return;

        File.Copy(storage.Backup, storage.Database);
        logger.LogInformation("Datenbank aus der Sicherung {Backup} geholt.", storage.Backup);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval, clock);

        do
        {
            try
            {
                BackupIfChanged();
                SnapshotIfDue();
                await CleanupIfDueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Eine gescheiterte Sicherung darf den Server nicht anhalten - beim nächsten Durchgang wieder.
                logger.LogError(ex, "Wartung der Datenbank fehlgeschlagen.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        // Beim Beenden das Letzte noch sichern - sonst fehlte es nach einem Neustart auf leerer Platte.
        try
        {
            BackupIfChanged();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Letzte Sicherung beim Beenden fehlgeschlagen.");
        }
    }

    /// <summary>Schreibt die laufende Kopie neu, wenn sich die Datenbank seit der letzten geändert hat.</summary>
    public void BackupIfChanged()
    {
        if (storage.Backup is null || !File.Exists(storage.Database))
            return;

        // SQLite schreibt Änderungen zuerst in die Nebendatei -wal und erst später
        // in die Hauptdatei - geändert ist, was an einer der beiden jünger ist.
        var wal = storage.Database + "-wal";
        var changed = File.Exists(wal)
            ? new[] { File.GetLastWriteTimeUtc(storage.Database), File.GetLastWriteTimeUtc(wal) }.Max()
            : File.GetLastWriteTimeUtc(storage.Database);

        if (changed <= lastBackup && File.Exists(storage.Backup))
            return;

        CopyTo(storage.Backup);
        lastBackup = changed;
    }

    /// <summary>Eine Sicherung je Tag; ältere als 14 Tage fallen weg.</summary>
    private void SnapshotIfDue()
    {
        if (!File.Exists(storage.Database))
            return;

        var today = clock.GetLocalNow().ToString("yyyy-MM-dd");
        var snapshot = Path.Combine(storage.SnapshotFolder, $"schoolmanager-{today}.db");

        if (!File.Exists(snapshot))
        {
            CopyTo(snapshot);
            logger.LogInformation("Tägliche Sicherung {Snapshot} angelegt.", snapshot);
        }

        var cutoff = clock.GetLocalNow().AddDays(-SnapshotDays).ToString("yyyy-MM-dd");

        foreach (var old in Directory.EnumerateFiles(storage.SnapshotFolder, "schoolmanager-*.db"))
        {
            var date = Path.GetFileNameWithoutExtension(old)["schoolmanager-".Length..];

            if (string.CompareOrdinal(date, cutoff) < 0)
                File.Delete(old);
        }
    }

    private async Task CleanupIfDueAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        if (now - lastCleanup < CleanupInterval)
            return;

        lastCleanup = now;

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDb>();

        var removed = await db.Sessions.Where(s => s.ExpiresAt < now).ExecuteDeleteAsync(cancellationToken);

        if (removed > 0)
            logger.LogInformation("{Count} abgelaufene Anmeldungen weggeräumt.", removed);
    }

    /// <summary>
    /// Kopiert über die Sicherungsfunktion von SQLite - auch während geschrieben
    /// wird, stimmig. Erst in eine neue Datei, dann an ihren Platz: Bricht es
    /// mittendrin ab, bleibt die bisherige Kopie heil.
    /// </summary>
    private void CopyTo(string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        var temporary = target + ".neu";

        using (var source = new SqliteConnection($"Data Source={storage.Database};Pooling=False"))
        using (var destination = new SqliteConnection($"Data Source={temporary};Pooling=False"))
        {
            source.Open();
            destination.Open();
            source.BackupDatabase(destination);
        }

        File.Move(temporary, target, overwrite: true);
    }
}
