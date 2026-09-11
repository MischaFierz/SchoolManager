using System.IO;
using System.Security.Cryptography;
using Microsoft.Identity.Client;
using SchoolManager.App.Data;
using SchoolManager.Core;

namespace SchoolManager.App;

/// <summary>
/// Meldet sich bei Microsoft 365 an und liefert Zugriffstoken für den
/// SMTP-Versand. Die Anmeldung läuft im Standardbrowser; das Token wird
/// verschlüsselt im Benutzerprofil zwischengespeichert, damit man sich nicht
/// bei jedem Start neu anmelden muss.
/// </summary>
public sealed class Microsoft365TokenSource(string clientId, string tenantId) : IAccessTokenSource
{
    private static readonly string[] Scopes = [SmtpSettings.Microsoft365SendScope];

    private static readonly string CacheFile = LocalStore.PathFor("m365-token.bin");

    private IPublicClientApplication? application;

    public async Task<string> GetAccessTokenAsync(string userName, CancellationToken cancellationToken = default)
    {
        var client = Build();
        var account = await FindAccountAsync(client, userName);

        if (account is not null)
        {
            try
            {
                var silent = await client.AcquireTokenSilent(Scopes, account)
                    .ExecuteAsync(cancellationToken);

                return silent.AccessToken;
            }
            catch (MsalUiRequiredException)
            {
                // Token abgelaufen oder Zustimmung nötig: unten interaktiv.
            }
        }

        return (await SignInAsync(userName, cancellationToken)).Token;
    }

    /// <summary>
    /// Meldet im Browser an und gibt zurück, wer angemeldet ist. Ohne
    /// <paramref name="hint"/> zeigt Microsoft die Kontoauswahl - so muss die
    /// Adresse des Postfachs nirgends vorher eingetippt werden.
    /// </summary>
    public async Task<SignIn> SignInAsync(string hint = "", CancellationToken cancellationToken = default)
    {
        var request = Build().AcquireTokenInteractive(Scopes).WithUseEmbeddedWebView(false);

        if (!string.IsNullOrWhiteSpace(hint))
            request = request.WithLoginHint(hint.Trim());

        var result = await request.ExecuteAsync(cancellationToken);

        return new SignIn(result.Account?.Username ?? "", result.AccessToken);
    }

    /// <summary>Wer angemeldet ist, samt frischem Token.</summary>
    public sealed record SignIn(string Mailbox, string Token);

    /// <summary>Das zuletzt angemeldete Postfach, oder leer.</summary>
    public async Task<string> SignedInMailboxAsync()
    {
        var accounts = await Build().GetAccountsAsync();

        return accounts.FirstOrDefault()?.Username ?? "";
    }

    /// <summary>Ist für dieses Postfach schon eine Anmeldung gespeichert?</summary>
    public async Task<bool> IsSignedInAsync(string userName) =>
        await FindAccountAsync(Build(), userName) is not null;

    /// <summary>Entfernt die gespeicherte Anmeldung.</summary>
    public async Task SignOutAsync()
    {
        var client = Build();

        foreach (var account in await client.GetAccountsAsync())
            await client.RemoveAsync(account);
    }

    private static async Task<IAccount?> FindAccountAsync(IPublicClientApplication client, string userName)
    {
        var accounts = (await client.GetAccountsAsync()).ToList();

        return accounts.FirstOrDefault(a =>
                   string.Equals(a.Username, userName, StringComparison.OrdinalIgnoreCase))
               ?? accounts.FirstOrDefault();
    }

    private IPublicClientApplication Build()
    {
        if (application is not null)
            return application;

        // Ohne Verzeichnis-ID gilt "common": Schul- und Geschäftskonten ebenso
        // wie private Microsoft-Konten. So passt jedes Konto, mit dem man sich
        // bei Microsoft anmelden kann.
        var authority = string.IsNullOrWhiteSpace(tenantId) ? "common" : tenantId.Trim();

        application = PublicClientApplicationBuilder
            .Create(clientId.Trim())
            .WithAuthority(AzureCloudInstance.AzurePublic, authority)
            .WithDefaultRedirectUri()
            .Build();

        AttachCache(application.UserTokenCache);

        return application;
    }

    /// <summary>
    /// Legt den Token-Zwischenspeicher als Datei ab, mit der Windows-
    /// Datenschutz-API verschlüsselt - lesbar nur für dieses Benutzerkonto.
    /// </summary>
    private static void AttachCache(ITokenCache cache)
    {
        cache.SetBeforeAccess(args =>
        {
            try
            {
                if (!File.Exists(CacheFile))
                    return;

                var protectedBytes = File.ReadAllBytes(CacheFile);
                var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                args.TokenCache.DeserializeMsalV3(bytes);
            }
            catch (Exception ex) when (ex is IOException or CryptographicException)
            {
                // Kein oder unbrauchbarer Zwischenspeicher: dann eben neu anmelden.
            }
        });

        cache.SetAfterAccess(args =>
        {
            if (!args.HasStateChanged)
                return;

            try
            {
                Directory.CreateDirectory(LocalStore.Folder);

                var bytes = args.TokenCache.SerializeMsalV3();
                var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(CacheFile, protectedBytes);
            }
            catch (Exception ex) when (ex is IOException or CryptographicException)
            {
                // Nicht speicherbar: die Anmeldung gilt dann nur für diese Sitzung.
            }
        });
    }
}
