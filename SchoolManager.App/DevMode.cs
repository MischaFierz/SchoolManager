using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SchoolManager.App.Data;
using SchoolManager.App.Logging;
using SchoolManager.App.Online;
using SchoolManager.App.Update;

namespace SchoolManager.App;

/// <summary>
/// Der Entwicklermodus. Er blendet ein, was für normale Nutzung nicht gedacht
/// ist - die Konto-Art Microsoft 365, Dev-Patches und das Protokoll.
///
/// Aufgeschaltet wird er, indem man unten in der Seitenleiste siebenmal auf die
/// Versionsnummer klickt und sich dann mit einem Konto anmeldet, das im
/// Admin-Panel dafür eingetragen ist. Welche Dev-Versionen es beziehen darf,
/// entscheidet der Server.
///
/// Einmal eingeschaltet, bleibt er es - über Neustarts und Updates hinweg -,
/// bis man ihn ausdrücklich verlässt. Nimmt der Server die Anmeldung nicht mehr
/// an, bleibt der Modus eingeschaltet und ist nur abgemeldet: Dev-Versionen und
/// Entwickler-Meldungen gibt es dann erst nach einer neuen Anmeldung.
/// </summary>
public static class DevMode
{
    /// <summary>So viele Klicks auf die Versionsnummer öffnen die Anmeldung.</summary>
    public const int ClicksToUnlock = 7;

    private static readonly string FilePath = LocalStore.PathFor("entwickler.json");

    private static State state = Load();

    /// <summary>Ist der Entwicklermodus eingeschaltet - also ein Entwickler angemeldet?</summary>
    public static bool IsEnabled => state.Enabled;

    /// <summary>
    /// Sollen Vorabversionen (Dev-Patches) statt der öffentlichen Releases
    /// bezogen werden? Ohne Entwicklermodus immer nein.
    /// </summary>
    public static bool UseDevPatches => state.Enabled && state.DevPatches;

    /// <summary>Die Anmeldung beim Server; nur im Entwicklermodus vorhanden.</summary>
    public static string? Token => state.Enabled && !string.IsNullOrEmpty(state.Token) ? state.Token : null;

    /// <summary>Eingeschaltet und beim Server angemeldet?</summary>
    public static bool IsSignedIn => Token is not null;

    /// <summary>Das angemeldete Konto, wie es der Server zuletzt beschrieben hat.</summary>
    public static DevAccount? Account => state.Enabled ? state.Account : null;

    /// <summary>
    /// Die beim Einschalten angelegte Sicherung der Daten; null, wenn keine
    /// zustande kam. Mit ihr geht es beim Verlassen wieder zurück.
    /// </summary>
    public static string? BackupPath => state.BackupPath;

    /// <summary>Meldet jede Änderung, damit die Oberfläche nachziehen kann.</summary>
    public static event Action? Changed;

    /// <summary>Meldet beim Server an und schaltet bei Erfolg den Entwicklermodus ein.</summary>
    /// <exception cref="ServerException">Mit einer Meldung, die sich so anzeigen lässt.</exception>
    public static async Task SignInAsync(string userName, string password)
    {
        var (token, account) = await ServerApi.SignInAsync(userName, password);

        var wasEnabled = state.Enabled;

        state.Enabled = true;
        state.Token = token;
        state.Account = account;

        if (!wasEnabled)
        {
            // Wer den Entwicklermodus einschaltet, will die Dev-Patches sehen; ohne
            // den Haken fände die Update-Suche nur das öffentliche Release.
            state.DevPatches = true;

            // Bevor irgendein Dev-Patch die Daten anfassen kann, kommt der ganze
            // Datenordner in eine Sicherung. Klappt das nicht, wird trotzdem
            // eingeschaltet - die Oberfläche sagt dann, dass es keine gibt.
            state.BackupPath = DevBackupService.TryCreate() ?? state.BackupPath;
        }

        Save();
    }

    /// <summary>Schaltet ab und meldet beim Server ab.</summary>
    public static void Disable()
    {
        if (!state.Enabled)
            return;

        if (state.Token is { Length: > 0 } token)
            _ = ServerApi.SignOutAsync(token);

        // Beim Abschalten auch den Patch-Kanal zurücksetzen; sonst zöge eine
        // später wieder eingeschaltete Installation stillschweigend Dev-Stände.
        state.Enabled = false;
        state.DevPatches = false;
        state.Token = null;
        state.Account = null;
        Save();
    }

    /// <summary>
    /// Fragt beim Server nach, ob die Anmeldung noch gilt, und holt das Konto
    /// frisch. Gilt sie nicht mehr - abgelaufen, Konto gesperrt oder gelöscht -,
    /// bleibt der Entwicklermodus eingeschaltet, ist aber abgemeldet; das
    /// Ergebnis sagt das. Ist der Server nur nicht erreichbar, bleibt alles,
    /// wie es ist - offline zu arbeiten soll niemanden hinauswerfen.
    /// </summary>
    public static async Task<string?> VerifyAsync()
    {
        if (!state.Enabled)
            return null;

        if (state.Token is not { Length: > 0 } token)
            return ServerApi.IsConfigured
                ? "Der Entwicklermodus ist eingeschaltet, aber nicht angemeldet - Anmelden unter Einstellungen → Entwickler."
                : null;

        DevAccount? account;

        try
        {
            account = await ServerApi.AccountAsync(token);
        }
        catch (ServerException ex)
        {
            AppLog.Error($"Die Anmeldung im Entwicklermodus liess sich nicht prüfen: {ex.Message}", "Server");
            return null;
        }

        if (account is null || !account.Permissions.Contains("DevMode"))
        {
            // Nur abmelden, nicht abschalten: Den Entwicklermodus verlässt man
            // ausdrücklich. Eine Abmeldung beim Server erübrigt sich.
            state.Token = null;
            state.Account = null;
            Save();

            return "Die Anmeldung im Entwicklermodus gilt nicht mehr - neu anmelden unter Einstellungen → Entwickler. Der Entwicklermodus bleibt eingeschaltet.";
        }

        if (account != state.Account)
        {
            state.Account = account;
            Save();
        }

        return null;
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
        Write(state);
        Changed?.Invoke();
    }

    private static void Write(State value)
    {
        try
        {
            value.ProtectedToken = Protect(value.Token);
            value.PlainToken = null;

            Directory.CreateDirectory(LocalStore.Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(value));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // Nicht speicherbar: dann gilt die Einstellung eben nur diesmal.
            AppLog.Error($"Der Entwicklermodus konnte nicht gespeichert werden: {ex.Message}", "Entwicklermodus");
        }
    }

    private static State Load()
    {
        State loaded;

        try
        {
            loaded = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath)) ?? new State()
                : new State();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new State();
        }

        loaded.Token = Unprotect(loaded.ProtectedToken) ?? loaded.PlainToken;

        // Eine Anmeldung aus der Zeit vor der Verschlüsselung gleich verschlüsselt ablegen.
        if (loaded.PlainToken is not null)
            Write(loaded);

        // Ein Entwicklermodus aus der Zeit ohne Anmeldung bleibt nach dem Update
        // eingeschaltet - er ist nur noch nicht angemeldet.
        return loaded;
    }

    /// <summary>
    /// Verschlüsselt mit der Windows-Datenschutz-API für dieses Windows-Konto -
    /// wie das Passwort des Mail-Kontos. Kopiert jemand die Datei auf einen
    /// anderen Rechner oder in ein anderes Konto, ist sie dort nutzlos.
    /// </summary>
    private static string? Protect(string? token) =>
        string.IsNullOrEmpty(token)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser));

    /// <summary>Null, wenn nichts gespeichert ist oder es sich nicht entschlüsseln lässt - dann ist man eben abgemeldet.</summary>
    private static string? Unprotect(string? protectedToken)
    {
        if (string.IsNullOrEmpty(protectedToken))
            return null;

        try
        {
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(Convert.FromBase64String(protectedToken), null, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }

    private sealed class State
    {
        public bool Enabled { get; set; }
        public bool DevPatches { get; set; }

        /// <summary>Die Sicherung vom Einschalten, für den Weg zurück.</summary>
        public string? BackupPath { get; set; }

        /// <summary>Die Anmeldung beim Server im Speicher - nie so in der Datei.</summary>
        [JsonIgnore]
        public string? Token { get; set; }

        /// <summary>Die Anmeldung, mit DPAPI für dieses Windows-Konto verschlüsselt.</summary>
        public string? ProtectedToken { get; set; }

        /// <summary>Nur zum Einlesen älterer Dateien, in denen die Anmeldung noch im Klartext stand.</summary>
        [JsonPropertyName("Token")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? PlainToken { get; set; }

        public DevAccount? Account { get; set; }
    }
}
