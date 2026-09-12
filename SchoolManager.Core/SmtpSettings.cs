using MailKit.Security;

namespace SchoolManager.Core;

/// <summary>Verschlüsselung der SMTP-Verbindung.</summary>
public enum SmtpSecurity
{
    /// <summary>Anhand des Ports entscheiden.</summary>
    Auto,

    /// <summary>Unverschlüsselt verbinden und dann auf TLS umschalten (üblich auf Port 587).</summary>
    StartTls,

    /// <summary>Verbindung direkt über TLS aufbauen (üblich auf Port 465).</summary>
    SslOnConnect,

    /// <summary>Ohne Verschlüsselung - nur für lokale Testserver.</summary>
    None
}

/// <summary>Art des Postfachs; bestimmt, wie sich die App anmeldet.</summary>
public enum MailAccountKind
{
    /// <summary>Beliebiger SMTP-Server mit Benutzername und Passwort.</summary>
    Smtp,

    /// <summary>Exchange-Server im Haus - SMTP mit Benutzername und Passwort.</summary>
    ExchangeOnPremises,

    /// <summary>Exchange Online in Microsoft 365 - Anmeldung über OAuth2.</summary>
    Microsoft365
}

/// <summary>Zugangsdaten und Verbindungsangaben für den Postausgang.</summary>
public sealed class SmtpSettings
{
    /// <summary>Voreinstellungen für Microsoft 365 (Exchange Online).</summary>
    public const string Microsoft365Host = "smtp.office365.com";

    public const int Microsoft365Port = 587;

    /// <summary>
    /// Berechtigung, die Microsoft 365 für den Versand verlangt. Gesendet wird
    /// über Microsoft Graph, nicht über SMTP - Exchange Online hat die
    /// SMTP-Anmeldung seit 2020 standardmässig abgeschaltet.
    /// </summary>
    public const string Microsoft365SendScope = "https://graph.microsoft.com/Mail.Send";

    /// <summary>
    /// Berechtigung, um das eigene Postfach zu lesen. Sie wird nicht zum Senden
    /// gebraucht, wohl aber für „Verbindung testen“ und die Anzeige, als wer man
    /// angemeldet ist: beides fragt bei Graph <c>/me</c> ab, und dafür genügt
    /// <see cref="Microsoft365SendScope"/> nicht - Graph antwortet sonst mit 403.
    /// </summary>
    public const string Microsoft365ProfileScope = "https://graph.microsoft.com/User.Read";

    /// <summary>Die Berechtigungen, die die Anmeldung anfordert.</summary>
    public static IReadOnlyList<string> Microsoft365Scopes { get; } =
        [Microsoft365SendScope, Microsoft365ProfileScope];

    /// <summary>
    /// Anwendungs-ID, die in School Manager eingebaut ist. Ist sie gesetzt,
    /// genügt in den Einstellungen ein Klick auf „Mit Microsoft anmelden“;
    /// ist sie leer, muss jeder seine eigene Azure-App-Registrierung eintragen.
    /// Siehe README, Abschnitt „Microsoft 365 einrichten“.
    /// </summary>
    public const string BuiltInClientId = "8f508d30-5a85-485e-a460-7b087f39537b";

    /// <summary>Ist eine Anwendungs-ID eingebaut, bleiben die Felder dafür verborgen.</summary>
    public static bool HasBuiltInClientId => BuiltInClientId.Length > 0;

    public MailAccountKind AccountKind { get; set; } = MailAccountKind.Smtp;

    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public SmtpSecurity Security { get; set; } = SmtpSecurity.Auto;

    /// <summary>Benutzername für die Anmeldung. Leer = ohne Authentifizierung senden.</summary>
    public string UserName { get; set; } = "";

    public string Password { get; set; } = "";

    /// <summary>
    /// Anwendungs-ID der Azure-App-Registrierung; nur für Microsoft 365 und nur
    /// nötig, wenn keine in School Manager eingebaut ist. Über
    /// <see cref="EffectiveClientId"/> abfragen, nicht direkt.
    /// </summary>
    public string ClientId { get; set; } = "";

    /// <summary>Die tatsächlich verwendete Anwendungs-ID: die eingebaute, sonst die eigene.</summary>
    public string EffectiveClientId =>
        HasBuiltInClientId ? BuiltInClientId : ClientId.Trim();

    /// <summary>
    /// Verzeichnis-ID (Tenant) der Schule; nur für Microsoft 365. Leer bedeutet
    /// "common", also jedes Konto, mit dem man sich bei Microsoft anmelden kann -
    /// Schul- und Geschäftskonten ebenso wie private.
    /// </summary>
    public string TenantId { get; set; } = "";

    /// <summary>Absender-Adresse; wenn leer, wird <see cref="UserName"/> verwendet.</summary>
    public string FromAddress { get; set; } = "";

    public string FromDisplayName { get; set; } = "";

    public string EffectiveFrom =>
        string.IsNullOrWhiteSpace(FromAddress) ? UserName : FromAddress;

    /// <summary>Microsoft 365 meldet sich mit einem Zugriffstoken an, nicht mit Passwort.</summary>
    public bool UsesOAuth => AccountKind == MailAccountKind.Microsoft365;

    internal SecureSocketOptions SocketOptions => Security switch
    {
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        SmtpSecurity.None => SecureSocketOptions.None,
        _ => SecureSocketOptions.Auto
    };

    /// <summary>Setzt Server, Port und Verschlüsselung passend zur Konto-Art.</summary>
    public void ApplyDefaultsForAccountKind()
    {
        switch (AccountKind)
        {
            case MailAccountKind.Microsoft365:
                Host = Microsoft365Host;
                Port = Microsoft365Port;
                Security = SmtpSecurity.StartTls;
                Password = "";
                break;

            case MailAccountKind.ExchangeOnPremises:
                Port = 587;
                Security = SmtpSecurity.StartTls;
                break;
        }
    }

    /// <summary>Wirft eine Ausnahme, wenn die Konfiguration unbrauchbar ist.</summary>
    public void Validate()
    {
        // Microsoft 365 geht über Graph: dort gibt es weder Server noch Port,
        // und das Postfach steht in der Anmeldung, nicht in einem Feld.
        if (UsesOAuth)
        {
            if (string.IsNullOrWhiteSpace(EffectiveClientId))
                throw new InvalidOperationException(
                    "Für Microsoft 365 fehlt die Anwendungs-ID (Client-ID) der Azure-App-Registrierung.");

            if (string.IsNullOrWhiteSpace(UserName))
                throw new InvalidOperationException(
                    "Noch nicht bei Microsoft angemeldet - bitte in den Einstellungen anmelden.");

            return;
        }

        if (string.IsNullOrWhiteSpace(Host))
            throw new InvalidOperationException("Es ist kein Server (Host) konfiguriert.");

        if (Port is < 1 or > 65535)
            throw new InvalidOperationException($"Der Port '{Port}' ist ungültig.");

        if (string.IsNullOrWhiteSpace(EffectiveFrom))
            throw new InvalidOperationException("Es ist weder eine Absender-Adresse noch ein Benutzername konfiguriert.");
    }

    public SmtpSettings Clone() => new()
    {
        AccountKind = AccountKind,
        Host = Host,
        Port = Port,
        Security = Security,
        UserName = UserName,
        Password = Password,
        ClientId = ClientId,
        TenantId = TenantId,
        FromAddress = FromAddress,
        FromDisplayName = FromDisplayName
    };
}
