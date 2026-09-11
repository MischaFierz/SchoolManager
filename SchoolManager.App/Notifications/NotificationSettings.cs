using System.IO;
using System.Text.Json;
using SchoolManager.App.Data;

namespace SchoolManager.App.Notifications;

/// <summary>
/// Was School Manager an Erinnerungen schicken darf und ob er dafür beim
/// Anmelden mitstarten soll.
///
/// Beides steht in einer eigenen kleinen Datei im Datenordner - nicht in den
/// E-Mail-Einstellungen, weil es nichts damit zu tun hat und auch dann gelten
/// muss, wenn nie ein Postausgang eingerichtet wurde.
/// </summary>
public static class NotificationSettings
{
    private static readonly string FilePath = LocalStore.PathFor("benachrichtigungen.json");

    private static State state = Load();

    /// <summary>Meldet jede Änderung, damit die Oberfläche nachziehen kann.</summary>
    public static event Action? Changed;

    /// <summary>Sollen Erinnerungen erscheinen? Voreinstellung: ja.</summary>
    public static bool Enabled => state.Enabled;

    /// <summary>Startet School Manager beim Anmelden im Hintergrund mit?</summary>
    public static bool AutoStart => state.AutoStart;

    /// <summary>
    /// Wurde beim ersten Start schon gefragt? Die Frage kommt genau einmal;
    /// danach steht die Antwort in den Einstellungen.
    /// </summary>
    public static bool WasAsked => state.WasAsked;

    /// <summary>Wie viele Tage im Voraus erinnert wird.</summary>
    public static int LeadDays => Math.Clamp(state.LeadDays, 0, 30);

    /// <summary>Wann zuletzt erinnert wurde - gegen ständige Wiederholung.</summary>
    public static DateTimeOffset? LastReminded => state.LastReminded;

    public static void SetEnabled(bool value)
    {
        if (state.Enabled == value)
            return;

        state.Enabled = value;
        Save();
    }

    public static void SetAutoStart(bool value)
    {
        if (state.AutoStart == value)
            return;

        state.AutoStart = value;
        Save();
    }

    public static void SetLeadDays(int value)
    {
        var days = Math.Clamp(value, 0, 30);

        if (state.LeadDays == days)
            return;

        state.LeadDays = days;
        Save();
    }

    /// <summary>Hält fest, dass die Frage beim ersten Start gestellt wurde.</summary>
    public static void MarkAsked()
    {
        if (state.WasAsked)
            return;

        state.WasAsked = true;
        Save();
    }

    /// <summary>Hält den Zeitpunkt der letzten Erinnerung fest.</summary>
    public static void MarkReminded()
    {
        state.LastReminded = DateTimeOffset.Now;
        Save();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(LocalStore.Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(state));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nicht speicherbar: dann gilt die Einstellung eben nur diesmal.
        }

        Changed?.Invoke();
    }

    private static State Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath)) ?? new State()
                : new State();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new State();
        }
    }

    private sealed class State
    {
        /// <summary>Voreinstellung ja - erinnert werden ist der übliche Fall.</summary>
        public bool Enabled { get; set; } = true;

        public bool AutoStart { get; set; } = true;

        public bool WasAsked { get; set; }

        public int LeadDays { get; set; } = 3;

        public DateTimeOffset? LastReminded { get; set; }
    }
}
