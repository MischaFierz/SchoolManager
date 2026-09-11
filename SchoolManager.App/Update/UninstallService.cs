using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using SchoolManager.App.Data;

namespace SchoolManager.App.Update;

/// <summary>
/// Setzt die Daten der App zurück und entfernt School Manager wieder vom
/// Rechner.
///
/// Beides erledigt ein kleines Aufräum-Skript statt der App selbst: ein
/// laufendes Programm kann sich weder löschen noch verhindern, dass die Seiten
/// beim Schliessen ihre Eingaben nochmals speichern. Das Skript wartet
/// deshalb, bis School Manager beendet ist, räumt dann auf und löscht sich
/// zuletzt selbst.
/// </summary>
public static class UninstallService
{
    /// <summary>Name, unter dem das Setup die App in der Registrierung einträgt.</summary>
    private const string ProductName = "School Manager";

    /// <summary>Der Ordner mit allen Daten der App.</summary>
    public static string DataFolder => LocalStore.Folder;

    /// <summary>Die laufende Programmdatei.</summary>
    public static string ProgramPath => Environment.ProcessPath ?? "";

    /// <summary>Die vom Setup eingetragene Installation, sonst null.</summary>
    public static Installation? Installed => FindInstallation();

    /// <summary>
    /// Löscht alle eigenen Daten und startet School Manager danach neu. Die
    /// Installation bleibt bestehen, die App beginnt wieder leer.
    /// </summary>
    public static void ResetDataAndRestart() =>
        RunCleanupAndExit("schoolmanager-zuruecksetzen.cmd", removeProgram: false, restart: true);

    /// <summary>
    /// Entfernt die Installation samt Verknüpfungen und alle Daten. Danach ist
    /// nichts von School Manager mehr auf dem Rechner.
    /// </summary>
    public static void UninstallAndExit() =>
        RunCleanupAndExit("schoolmanager-deinstallieren.cmd", removeProgram: true, restart: false);

    /// <summary>Was die Deinstallation entfernen wird - für die Rückfrage.</summary>
    public static IReadOnlyList<string> WhatWillBeRemoved()
    {
        var items = new List<string>();

        if (FindInstallation() is { } installation)
        {
            items.Add(installation.Location.Length > 0
                ? $"die Installation in {installation.Location} samt Startmenü- und Desktop-Verknüpfung"
                : "die Installation samt Startmenü- und Desktop-Verknüpfung");
        }
        else if (ProgramPath.Length > 0)
        {
            items.Add($"die Programmdatei {ProgramPath}");
        }

        items.Add($"alle Daten in {DataFolder} - Aufträge, Aufgaben, Hausaufgaben, Prüfungen, "
                  + "Lehrkräfte, Stundenplan, To-Do und Notizen");
        items.Add("die Einstellungen samt gespeicherter Anmeldung bei Microsoft 365");

        return items;
    }

    /// <summary>Schreibt das Aufräum-Skript, startet es und beendet die App.</summary>
    private static void RunCleanupAndExit(string scriptName, bool removeProgram, bool restart)
    {
        var script = WriteCleanupScript(scriptName, removeProgram, restart);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        });

        Application.Current.Shutdown();
    }

    private static string WriteCleanupScript(string scriptName, bool removeProgram, bool restart)
    {
        var script = Path.Combine(Path.GetTempPath(), scriptName);
        var installation = removeProgram ? FindInstallation() : null;
        var pid = Environment.ProcessId;

        var text = new StringBuilder()
            .AppendLine("@echo off")
            .AppendLine("rem Raeumt auf, sobald School Manager beendet ist.")
            .AppendLine(":warten")
            .AppendLine($"tasklist /fi \"PID eq {pid}\" /nh 2>nul | find \"{pid}\" >nul")
            .AppendLine("if not errorlevel 1 (")
            // ping als Pause: timeout braucht eine Eingabeaufforderung, die es
            // im versteckt gestarteten Skript nicht gibt.
            .AppendLine("    ping -n 2 127.0.0.1 >nul")
            .AppendLine("    goto warten")
            .AppendLine(")");

        if (removeProgram)
        {
            if (installation is { } found)
            {
                // /qb zeigt nur den Fortschritt; gefragt wurde bereits in der App.
                text.AppendLine($"msiexec /x {found.ProductCode} /qb");

                if (found.Location.Length > 0)
                    text.AppendLine(Remove(found.Location));
            }
            else if (ProgramPath.Length > 0)
            {
                // Eine einfach kopierte EXE hat kein Setup.
                text.AppendLine($"del /f /q \"{ProgramPath}\"");
            }
        }

        text.AppendLine(Remove(DataFolder));
        // Heruntergeladene Installationspakete der Update-Suche; UpdateService
        // legt sie als SchoolManagerSetup-<Version>.msi im Temp-Ordner ab.
        text.AppendLine($"del /f /q \"{Path.Combine(Path.GetTempPath(), "SchoolManagerSetup-*.msi")}\"");

        if (restart && ProgramPath.Length > 0)
            text.AppendLine($"start \"\" \"{ProgramPath}\"");

        // Zum Schluss loescht sich das Skript selbst.
        text.AppendLine("(goto) 2>nul & del \"%~f0\"");

        File.WriteAllText(script, text.ToString(), Encoding.Default);

        return script;

        static string Remove(string folder) => $"rmdir /s /q \"{folder.TrimEnd(Path.DirectorySeparatorChar)}\"";
    }

    /// <summary>
    /// Sucht den Eintrag des Setups in der Registrierung. Das Paket wird ins
    /// Benutzerprofil installiert, steht also unter HKEY_CURRENT_USER; ein für
    /// alle Benutzer installiertes Paket stünde unter HKEY_LOCAL_MACHINE.
    /// </summary>
    private static Installation? FindInstallation()
    {
        const string path = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

        RegistryKey[] roots =
        [
            RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default),
            RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64),
            RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
        ];

        foreach (var root in roots)
        {
            using (root)
            {
                using var list = root.OpenSubKey(path);

                if (list is null)
                    continue;

                foreach (var name in list.GetSubKeyNames())
                {
                    using var entry = list.OpenSubKey(name);

                    if (entry?.GetValue("DisplayName") as string != ProductName)
                        continue;

                    return new Installation(
                        name,
                        entry.GetValue("InstallLocation") as string ?? "",
                        entry.GetValue("DisplayVersion") as string ?? "");
                }
            }
        }

        return null;
    }

    /// <summary>Eine vom Setup eingetragene Installation.</summary>
    /// <param name="ProductCode">Der Schlüssel in der Registrierung, für msiexec.</param>
    public sealed record Installation(string ProductCode, string Location, string Version);
}
