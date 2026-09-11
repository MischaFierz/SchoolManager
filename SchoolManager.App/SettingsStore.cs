using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SchoolManager.App.Data;
using SchoolManager.Core;

namespace SchoolManager.App;

/// <summary>
/// Liest und schreibt die SMTP-Einstellungen im Benutzerprofil.
/// Das Passwort wird mit der Windows-Datenschutz-API (DPAPI) verschlüsselt und
/// ist damit nur für das angemeldete Windows-Konto lesbar.
/// </summary>
public static class SettingsStore
{
    public static string FilePath { get; } = LocalStore.PathFor("settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static SmtpSettings Load() => Load(FilePath);

    /// <summary>Liest Einstellungen aus einer beliebigen Datei, etwa einer importierten Konfiguration.</summary>
    public static SmtpSettings Load(string path)
    {
        if (!File.Exists(path))
            return new SmtpSettings();

        try
        {
            var stored = JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(path), JsonOptions);

            if (stored is null)
                return new SmtpSettings();

            return new SmtpSettings
            {
                AccountKind = Enum.TryParse<MailAccountKind>(stored.AccountKind, ignoreCase: true, out var kind)
                    ? kind
                    : MailAccountKind.Smtp,
                ClientId = stored.ClientId,
                TenantId = stored.TenantId,
                Host = stored.Host,
                Port = stored.Port,
                Security = Enum.TryParse<SmtpSecurity>(stored.Security, ignoreCase: true, out var security)
                    ? security
                    : SmtpSecurity.Auto,
                UserName = stored.UserName,
                Password = Unprotect(stored.ProtectedPassword),
                FromAddress = stored.FromAddress,
                FromDisplayName = stored.FromDisplayName
            };
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Eine beschädigte Datei soll den Start nicht verhindern.
            return new SmtpSettings();
        }
    }

    public static void Save(SmtpSettings settings)
    {
        var stored = new StoredSettings
        {
            AccountKind = settings.AccountKind.ToString(),
            ClientId = settings.ClientId,
            TenantId = settings.TenantId,
            Host = settings.Host,
            Port = settings.Port,
            Security = settings.Security.ToString(),
            UserName = settings.UserName,
            ProtectedPassword = Protect(settings.Password),
            FromAddress = settings.FromAddress,
            FromDisplayName = settings.FromDisplayName
        };

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(stored, JsonOptions));
    }

    private static string? Protect(string password)
    {
        if (string.IsNullOrEmpty(password))
            return null;

        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(password), null, DataProtectionScope.CurrentUser);

        return Convert.ToBase64String(encrypted);
    }

    private static string Unprotect(string? protectedPassword)
    {
        if (string.IsNullOrEmpty(protectedPassword))
            return "";

        try
        {
            var decrypted = ProtectedData.Unprotect(
                Convert.FromBase64String(protectedPassword), null, DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // Etwa nach einem Wechsel des Windows-Kontos: dann muss das
            // Passwort neu eingegeben werden.
            return "";
        }
    }

    /// <summary>Abbild der Einstellungen auf der Festplatte.</summary>
    private sealed class StoredSettings
    {
        public string AccountKind { get; set; } = nameof(MailAccountKind.Smtp);
        public string ClientId { get; set; } = "";
        public string TenantId { get; set; } = "";
        public string Host { get; set; } = "";
        public int Port { get; set; } = 587;
        public string Security { get; set; } = nameof(SmtpSecurity.Auto);
        public string UserName { get; set; } = "";
        public string? ProtectedPassword { get; set; }
        public string FromAddress { get; set; } = "";
        public string FromDisplayName { get; set; } = "";
    }
}
