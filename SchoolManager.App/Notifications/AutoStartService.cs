using System.IO;
using System.Security;
using Microsoft.Win32;

namespace SchoolManager.App.Notifications;

/// <summary>
/// Trägt School Manager beim Anmelden in Windows ein, damit die Erinnerungen
/// auch ohne geöffnetes Fenster kommen.
///
/// Eingetragen wird unter HKEY_CURRENT_USER - das gilt nur für dieses
/// Benutzerkonto und braucht keine Administratorrechte, genau wie die
/// Installation selbst. Gestartet wird mit <see cref="BackgroundArgument"/>:
/// Dann läuft nur das Symbol im Infobereich mit, kein Fenster springt beim
/// Anmelden auf.
/// </summary>
public static class AutoStartService
{
    /// <summary>Damit gestartet, bleibt das Fenster zu.</summary>
    public const string BackgroundArgument = "--hintergrund";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "School Manager";

    /// <summary>Steht der Eintrag - und zeigt er auf die laufende Programmdatei?</summary>
    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);

                return key?.GetValue(ValueName) is string value && value.Length > 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                return false;
            }
        }
    }

    /// <summary>Trägt den Start ein; gibt die Fehlermeldung zurück, falls es nicht klappt.</summary>
    public static string? TryEnable()
    {
        if (ProgramPath.Length == 0)
            return "Die Programmdatei lässt sich nicht bestimmen.";

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);

            key.SetValue(ValueName, $"\"{ProgramPath}\" {BackgroundArgument}");

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return ex.Message;
        }
    }

    /// <summary>Entfernt den Eintrag wieder; gibt die Fehlermeldung zurück, falls es nicht klappt.</summary>
    public static string? TryDisable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);

            key?.DeleteValue(ValueName, throwOnMissingValue: false);

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Bringt den Eintrag in Windows mit der Einstellung in Übereinstimmung -
    /// auch nach einem Update, das die Programmdatei ersetzt hat.
    /// </summary>
    public static void Apply()
    {
        if (NotificationSettings.Enabled && NotificationSettings.AutoStart)
            TryEnable();
        else
            TryDisable();
    }

    private static string ProgramPath => Environment.ProcessPath ?? "";
}
