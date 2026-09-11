using System.IO;
using System.Text.Json;
using SchoolManager.App.Data;

namespace SchoolManager.App;

/// <summary>
/// Der Entwicklermodus. Er blendet ein, was für normale Nutzung nicht gedacht
/// ist - zurzeit die Konto-Art Microsoft 365 und den Bezug von Dev-Patches.
///
/// Das ist bewusst nur eine Sichtbarkeitsfrage, keine Zugriffssperre: Was in
/// der Anwendung steckt, lässt sich ohnehin auslesen. Es geht darum, Unfertiges
/// niemandem versehentlich vor die Nase zu setzen.
///
/// Aufgeschaltet wird er, indem man unten in der Seitenleiste siebenmal auf die
/// Versionsnummer klickt.
/// </summary>
public static class DevMode
{
    /// <summary>So viele Klicks auf die Versionsnummer schalten ihn frei.</summary>
    public const int ClicksToUnlock = 7;

    private static readonly string FilePath = LocalStore.PathFor("entwickler.json");

    private static State state = Load();

    /// <summary>Ist der Entwicklermodus eingeschaltet?</summary>
    public static bool IsEnabled => state.Enabled;

    /// <summary>
    /// Sollen Vorabversionen (Dev-Patches) statt der öffentlichen Releases
    /// bezogen werden? Ohne Entwicklermodus immer nein.
    /// </summary>
    public static bool UseDevPatches => state.Enabled && state.DevPatches;

    /// <summary>Meldet jede Änderung, damit die Oberfläche nachziehen kann.</summary>
    public static event Action? Changed;

    public static void Enable()
    {
        if (state.Enabled)
            return;

        state.Enabled = true;
        Save();
    }

    public static void Disable()
    {
        if (!state.Enabled)
            return;

        // Beim Abschalten auch den Patch-Kanal zurücksetzen; sonst zöge eine
        // später wieder eingeschaltete Installation stillschweigend Dev-Stände.
        state.Enabled = false;
        state.DevPatches = false;
        Save();
    }

    public static void SetDevPatches(bool value)
    {
        if (state.DevPatches == value)
            return;

        state.DevPatches = value;
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
        public bool Enabled { get; set; }
        public bool DevPatches { get; set; }
    }
}
