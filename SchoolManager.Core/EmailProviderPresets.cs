namespace SchoolManager.Core;

/// <summary>
/// Server, Port und Verschlüsselung gängiger Postfach-Anbieter, erkannt an der
/// Domäne der E-Mail-Adresse - damit müssen Nutzer diese Angaben bei bekannten
/// Anbietern nicht mehr von Hand nachschlagen und eintragen.
/// </summary>
public static class EmailProviderPresets
{
    public sealed record Preset(string Host, int Port, SmtpSecurity Security);

    private static readonly Dictionary<string, Preset> ByDomain = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gmail.com"] = new Preset("smtp.gmail.com", 587, SmtpSecurity.StartTls),
        ["googlemail.com"] = new Preset("smtp.gmail.com", 587, SmtpSecurity.StartTls),
        ["outlook.com"] = new Preset("smtp-mail.outlook.com", 587, SmtpSecurity.StartTls),
        ["hotmail.com"] = new Preset("smtp-mail.outlook.com", 587, SmtpSecurity.StartTls),
        ["live.com"] = new Preset("smtp-mail.outlook.com", 587, SmtpSecurity.StartTls),
        ["yahoo.com"] = new Preset("smtp.mail.yahoo.com", 587, SmtpSecurity.StartTls),
        ["yahoo.de"] = new Preset("smtp.mail.yahoo.com", 587, SmtpSecurity.StartTls),
        ["icloud.com"] = new Preset("smtp.mail.me.com", 587, SmtpSecurity.StartTls),
        ["me.com"] = new Preset("smtp.mail.me.com", 587, SmtpSecurity.StartTls),
        ["gmx.net"] = new Preset("mail.gmx.net", 587, SmtpSecurity.StartTls),
        ["gmx.de"] = new Preset("mail.gmx.net", 587, SmtpSecurity.StartTls),
        ["gmx.ch"] = new Preset("mail.gmx.net", 587, SmtpSecurity.StartTls),
        ["web.de"] = new Preset("smtp.web.de", 587, SmtpSecurity.StartTls),
        ["bluewin.ch"] = new Preset("smtpauths.bluewin.ch", 465, SmtpSecurity.SslOnConnect),
        ["sunrise.ch"] = new Preset("mail.sunrise.ch", 587, SmtpSecurity.StartTls),
        ["hispeed.ch"] = new Preset("mail.hispeed.ch", 587, SmtpSecurity.StartTls)
    };

    /// <summary>Versucht, aus einer E-Mail-Adresse Server, Port und Verschlüsselung zu bestimmen.</summary>
    public static bool TryGet(string emailAddress, out Preset preset)
    {
        preset = null!;

        var at = emailAddress.LastIndexOf('@');

        if (at < 0 || at == emailAddress.Length - 1)
            return false;

        var domain = emailAddress[(at + 1)..].Trim();

        return ByDomain.TryGetValue(domain, out preset!);
    }
}
