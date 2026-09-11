using SchoolManager.Core;

namespace SchoolManager.App;

/// <summary>
/// Hält die aktuell gültigen Postausgangs-Einstellungen und gibt Änderungen
/// bekannt, damit E-Mail-Seite und Kopfzeile denselben Stand anzeigen.
/// </summary>
public sealed class SmtpSettingsService
{
    public SmtpSettings Current { get; private set; } = SettingsStore.Load();

    public event Action? Changed;

    // Bei Microsoft 365 gibt es keinen Server; dort zaehlt die Anmeldung.
    public bool IsConfigured => Current.UsesOAuth
        ? !string.IsNullOrWhiteSpace(Current.EffectiveClientId) && !string.IsNullOrWhiteSpace(Current.UserName)
        : !string.IsNullOrWhiteSpace(Current.Host) && !string.IsNullOrWhiteSpace(Current.EffectiveFrom);

    /// <summary>Speichert die Einstellungen und meldet die Änderung.</summary>
    public void Update(SmtpSettings settings)
    {
        SettingsStore.Save(settings);
        Current = settings;
        Changed?.Invoke();
    }

    /// <summary>
    /// Liefert die Anmeldung für Microsoft 365 - oder null, wenn das Konto mit
    /// Benutzername und Passwort arbeitet.
    /// </summary>
    public static IAccessTokenSource? CreateTokenSource(SmtpSettings settings) =>
        settings.UsesOAuth ? new Microsoft365TokenSource(settings.EffectiveClientId, settings.TenantId) : null;

    public IAccessTokenSource? CreateTokenSource() => CreateTokenSource(Current);
}
