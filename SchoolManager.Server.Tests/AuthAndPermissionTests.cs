using System.Net;

namespace SchoolManager.Server.Tests;

/// <summary>Anmeldung, Sperren und die Regeln gegen das Ausweiten von Rechten.</summary>
public sealed class AuthAndPermissionTests(ServerFactory factory) : IClassFixture<ServerFactory>
{
    private readonly HttpClient client = factory.CreateApp();

    [Fact]
    public async Task Falsches_Passwort_und_unbekannter_Benutzer_melden_dasselbe()
    {
        var (wrongStatus, wrong) = await client.SendAsync(HttpMethod.Post, "/api/auth/login",
            new { userName = "admin", password = "falsch-falsch", client = "panel" });
        var (unknownStatus, unknown) = await client.SendAsync(HttpMethod.Post, "/api/auth/login",
            new { userName = "gibtsnicht", password = "x", client = "panel" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongStatus);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownStatus);
        Assert.Equal(wrong.GetProperty("error").GetString(), unknown.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Ohne_Anmeldung_kein_Zugriff_aufs_Panel()
    {
        var (status, _) = await client.SendAsync(HttpMethod.Get, "/api/admin/users");
        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Fact]
    public async Task Benutzerverwalter_kann_sich_keine_Rechte_verschaffen()
    {
        var admin = await client.SignInAdminAsync();
        var (managerId, password) = await client.CreateUserAsync(admin, "verwalter", extra: ["Users"]);
        var manager = await client.SignInAsync("verwalter", password);

        var asAdmin = await client.SendAsync(HttpMethod.Post, "/api/admin/users",
            new { userName = "boese1", level = "Administrator", isActive = true, password = "Sehr-sicheres-Passwort-42" }, manager);
        var foreignRight = await client.SendAsync(HttpMethod.Post, "/api/admin/users",
            new { userName = "boese2", level = "Tester", extraPermissions = new[] { "Release" }, isActive = true, password = "Sehr-sicheres-Passwort-42" }, manager);
        var selfUpgrade = await client.SendAsync(HttpMethod.Put, $"/api/admin/users/{managerId}",
            new { displayName = "", level = "Tester", extraPermissions = new[] { "Users", "Release" }, isActive = true }, manager);
        var adminPassword = await client.SendAsync(HttpMethod.Post, "/api/admin/users/1/password",
            new { generatePassword = true }, manager);

        Assert.Equal(HttpStatusCode.Forbidden, asAdmin.Status);
        Assert.Equal(HttpStatusCode.Forbidden, foreignRight.Status);
        Assert.Equal(HttpStatusCode.Forbidden, selfUpgrade.Status);
        Assert.Equal(HttpStatusCode.Forbidden, adminPassword.Status);
    }

    [Fact]
    public async Task Eigenes_Konto_bleibt_Administrator_und_lässt_sich_nicht_löschen()
    {
        var admin = await client.SignInAdminAsync();

        var demote = await client.SendAsync(HttpMethod.Put, "/api/admin/users/1",
            new { displayName = "Administrator", level = "Tester", isActive = true }, admin);
        var delete = await client.SendAsync(HttpMethod.Delete, "/api/admin/users/1", token: admin);

        Assert.Equal(HttpStatusCode.BadRequest, demote.Status);
        Assert.Equal(HttpStatusCode.BadRequest, delete.Status);
    }

    [Fact]
    public async Task Nach_fünf_Fehlversuchen_ist_das_Konto_gesperrt_bis_es_entsperrt_wird()
    {
        var admin = await client.SignInAdminAsync();
        var (id, password) = await client.CreateUserAsync(admin, "sperre");

        for (var i = 0; i < 5; i++)
            await client.SendAsync(HttpMethod.Post, "/api/auth/login", new { userName = "sperre", password = "falsch", client = "app" });

        var locked = await client.SendAsync(HttpMethod.Post, "/api/auth/login", new { userName = "sperre", password, client = "app" });
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.Status);

        await client.SendAsync(HttpMethod.Post, $"/api/admin/users/{id}/unlock", token: admin);
        Assert.False(string.IsNullOrEmpty(await client.SignInAsync("sperre", password, "app")));
    }

    [Fact]
    public async Task Gesperrtes_Konto_ist_sofort_überall_abgemeldet()
    {
        var admin = await client.SignInAdminAsync();
        var (id, password) = await client.CreateUserAsync(admin, "gesperrt");
        var app = await client.SignInAsync("gesperrt", password, "app");

        var (status, _) = await client.SendAsync(HttpMethod.Put, $"/api/admin/users/{id}",
            new { displayName = "gesperrt", level = "Tester", isActive = false }, admin);
        Assert.Equal(HttpStatusCode.NoContent, status);

        var me = await client.SendAsync(HttpMethod.Get, "/api/me", token: app);
        Assert.Equal(HttpStatusCode.Unauthorized, me.Status);
    }

    [Fact]
    public async Task Schwaches_Passwort_wird_abgelehnt()
    {
        var admin = await client.SignInAdminAsync();

        var (status, _) = await client.SendAsync(HttpMethod.Post, "/api/me/password",
            new { currentPassword = ServerFactory.AdminPassword, newPassword = "kurz" }, admin);

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }
}
