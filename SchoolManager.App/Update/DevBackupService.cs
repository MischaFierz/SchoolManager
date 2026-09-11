using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using SchoolManager.App.Data;

namespace SchoolManager.App.Update;

/// <summary>
/// Der Sicherheitsgurt des Entwicklermodus.
///
/// Beim Einschalten wird der gesamte Datenordner als ZIP-Datei weggelegt. Beim
/// Verlassen lässt sich damit der Stand von vorher wiederherstellen: die letzte
/// öffentliche Version wird installiert und die Sicherung eingespielt. Dev-
/// Patches sind ungetestet und können Daten in einer Form hinterlassen, die
/// eine ältere Version nicht mehr versteht - darum gehört beides zusammen.
///
/// Eingespielt wird - wie beim Deinstallieren - von einem kleinen Skript statt
/// von der App selbst: Eine laufende Anwendung kann sich nicht selbst ersetzen,
/// und die Seiten würden beim Schliessen ihre Eingaben nochmals speichern und
/// die eben eingespielte Sicherung gleich wieder überschreiben.
/// </summary>
public static class DevBackupService
{
    /// <summary>
    /// Hier liegen die Sicherungen - bewusst neben dem Datenordner und nicht
    /// darin, sonst würden sie beim Wiederherstellen selbst gelöscht.
    /// </summary>
    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SchoolManager", "Sicherungen");

    /// <summary>
    /// Legt eine Sicherung des Datenordners an und gibt ihren Pfad zurück;
    /// null, wenn es keine Daten gibt oder das Schreiben nicht klappt. Ein
    /// Fehlschlag darf das Einschalten nicht aufhalten - gemeldet wird er in
    /// der Oberfläche.
    /// </summary>
    public static string? TryCreate()
    {
        try
        {
            Directory.CreateDirectory(Folder);

            var path = Path.Combine(Folder, $"vor-dev-{DateTime.Now:yyyy-MM-dd-HHmm}.zip");

            DataExportService.ExportAll(path);

            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Gibt es die Sicherung noch?</summary>
    public static bool Exists(string? path) => path is { Length: > 0 } && File.Exists(path);

    /// <summary>
    /// Startet das Skript, das die öffentliche Version installiert, die
    /// Sicherung einspielt und School Manager danach wieder startet; die App
    /// beendet sich dabei.
    /// </summary>
    /// <param name="installerPath">Das bereits heruntergeladene Installationspaket.</param>
    /// <param name="backupPath">Die Sicherung, oder null - dann bleiben die Daten, wie sie sind.</param>
    public static void RestoreAndExit(string installerPath, string? backupPath)
    {
        var script = WriteScript(installerPath, Exists(backupPath) ? backupPath : null);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        });

        Application.Current.Shutdown();
    }

    private static string WriteScript(string installerPath, string? backupPath)
    {
        var script = Path.Combine(Path.GetTempPath(), "schoolmanager-dev-zurueck.cmd");
        var installation = UninstallService.Installed;
        var pid = Environment.ProcessId;

        var text = new StringBuilder()
            .AppendLine("@echo off")
            .AppendLine("rem Stellt den Stand von vor dem Entwicklermodus wieder her.")
            .AppendLine(":warten")
            .AppendLine($"tasklist /fi \"PID eq {pid}\" /nh 2>nul | find \"{pid}\" >nul")
            .AppendLine("if not errorlevel 1 (")
            // ping als Pause: timeout braucht eine Eingabeaufforderung, die es
            // im versteckt gestarteten Skript nicht gibt.
            .AppendLine("    ping -n 2 127.0.0.1 >nul")
            .AppendLine("    goto warten")
            .AppendLine(")");

        // Der Dev-Patch trägt eine höhere Versionsnummer; das Setup lehnt die
        // ältere öffentliche Version deshalb als Rückschritt ab. Darum wird die
        // vorhandene Installation zuerst entfernt und danach neu installiert.
        if (installation is { } found)
            text.AppendLine($"msiexec /x {found.ProductCode} /qn");

        text.AppendLine($"msiexec /i \"{installerPath}\" /qb");

        if (backupPath is not null)
        {
            var folder = LocalStore.Folder.TrimEnd(Path.DirectorySeparatorChar);

            text.AppendLine($"rmdir /s /q \"{folder}\"");
            text.AppendLine("powershell -NoProfile -ExecutionPolicy Bypass -Command "
                            + $"\"Expand-Archive -LiteralPath '{backupPath}' -DestinationPath '{folder}' -Force\"");
        }

        text.AppendLine($"start \"\" \"{RestartPath(installation)}\"");

        // Zum Schluss loescht sich das Skript selbst.
        text.AppendLine("(goto) 2>nul & del \"%~f0\"");

        File.WriteAllText(script, text.ToString(), Encoding.Default);

        return script;
    }

    /// <summary>
    /// Die Programmdatei, die nach dem Einspielen gestartet wird. Das Setup
    /// installiert ins Benutzerprofil; die laufende Datei taugt nicht als Ziel,
    /// sie wird gerade ersetzt.
    /// </summary>
    private static string RestartPath(UninstallService.Installation? installation)
    {
        if (installation is { Location.Length: > 0 } found)
            return Path.Combine(found.Location, "SchoolManager.exe");

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "School Manager", "SchoolManager.exe");
    }
}
