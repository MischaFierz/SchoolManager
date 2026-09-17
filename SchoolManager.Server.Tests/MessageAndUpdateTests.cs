using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SchoolManager.Server.Services;

namespace SchoolManager.Server.Tests;

/// <summary>Meldungen an ihren Orten, die Update-Suche mit Freigaben und die Sicherung der Datenbank.</summary>
public sealed class MessageAndUpdateTests(ServerFactory factory) : IClassFixture<ServerFactory>
{
    private readonly HttpClient client = factory.CreateApp();

    private async Task<int> CreateMessageAsync(string admin, string text, string[] placements, string appPage = "", string audience = "Everyone")
    {
        var (status, body) = await client.SendAsync(HttpMethod.Post, "/api/admin/messages", new
        {
            text, kind = "Info", audience, isActive = true, expiresAt = (DateTimeOffset?)null, placements, appPage
        }, admin);

        Assert.Equal(HttpStatusCode.OK, status);
        return body.GetProperty("id").GetInt32();
    }

    private static List<string> Texts(JsonElement list) =>
        list.EnumerateArray().Select(m => m.GetProperty("text").GetString()!).ToList();

    [Fact]
    public async Task Meldungen_erscheinen_nur_an_ihrem_Ort()
    {
        var admin = await client.SignInAdminAsync();

        await CreateMessageAsync(admin, "App-überall", ["App"]);
        await CreateMessageAsync(admin, "App-Kalender", ["App"], "calendar");
        await CreateMessageAsync(admin, "Nur-Entwickler", ["App"], audience: "Developers");
        await CreateMessageAsync(admin, "Web-überall", ["StartPage", "SignInPage"]);
        await CreateMessageAsync(admin, "Nur-Startseite", ["StartPage"]);

        var app = Texts((await client.SendAsync(HttpMethod.Get, "/api/app/messages")).Body);
        var start = Texts((await client.SendAsync(HttpMethod.Get, "/api/public/messages?page=start")).Body);
        var signin = Texts((await client.SendAsync(HttpMethod.Get, "/api/public/messages?page=signin")).Body);

        Assert.Contains("App-überall", app);
        Assert.Contains("App-Kalender", app);
        Assert.DoesNotContain("Nur-Entwickler", app);
        Assert.DoesNotContain("Web-überall", app);

        Assert.Contains("Web-überall", start);
        Assert.Contains("Nur-Startseite", start);
        Assert.DoesNotContain("App-überall", start);

        Assert.Contains("Web-überall", signin);
        Assert.DoesNotContain("Nur-Startseite", signin);
        Assert.DoesNotContain("Nur-Entwickler", signin);
    }

    [Fact]
    public async Task Unbekannte_Seite_und_Meldung_ohne_Ort_werden_abgelehnt()
    {
        var admin = await client.SignInAdminAsync();

        var page = await client.SendAsync(HttpMethod.Post, "/api/admin/messages",
            new { text = "x", kind = "Info", audience = "Everyone", isActive = true, placements = new[] { "App" }, appPage = "gibtsnicht" }, admin);
        var nowhere = await client.SendAsync(HttpMethod.Post, "/api/admin/messages",
            new { text = "x", kind = "Info", audience = "Everyone", isActive = true, placements = Array.Empty<string>() }, admin);

        Assert.Equal(HttpStatusCode.BadRequest, page.Status);
        Assert.Equal(HttpStatusCode.BadRequest, nowhere.Status);
    }

    [Fact]
    public async Task Ohne_Anmeldung_gibt_es_das_neueste_Release_mit_Update_Info()
    {
        var admin = await client.SignInAdminAsync();
        await client.SendAsync(HttpMethod.Put, "/api/admin/releases/v1.2.0/note", new { text = "Neues Einstellungsmenü" }, admin);

        var (_, body) = await client.SendAsync(HttpMethod.Get, "/api/app/update?current=1.1.5");
        var update = body.GetProperty("update");

        Assert.Equal("v1.2.0", update.GetProperty("tag").GetString());
        Assert.Equal("Neues Einstellungsmenü", update.GetProperty("note").GetString());
    }

    [Fact]
    public async Task Tester_bekommt_Dev_Versionen_nur_mit_Freigabe()
    {
        var admin = await client.SignInAdminAsync();
        var (id, password) = await client.CreateUserAsync(admin, "tester");
        var tester = await client.SignInAsync("tester", password, "app");

        var before = (await client.SendAsync(HttpMethod.Get, "/api/app/update?current=1.2.0&dev=true", token: tester)).Body;
        Assert.Equal(JsonValueKind.Null, before.GetProperty("update").ValueKind);

        await client.SendAsync(HttpMethod.Put, "/api/admin/releases/v1.2.1-dev/approval",
            new { forAllDevelopers = false, userIds = new[] { id }, groupIds = Array.Empty<int>() }, admin);

        var after = (await client.SendAsync(HttpMethod.Get, "/api/app/update?current=1.2.0&dev=true", token: tester)).Body;
        Assert.Equal("v1.2.1-dev", after.GetProperty("update").GetProperty("tag").GetString());

        var download = await client.SendAsync(HttpMethod.Get, "/api/app/download/v1.2.1-dev", token: tester);
        Assert.StartsWith("https://objects.githubusercontent.com/", download.Body.GetProperty("url").GetString());

        var notApproved = await client.SendAsync(HttpMethod.Get, "/api/app/download/v1.3.0-dev", token: tester);
        Assert.Equal(HttpStatusCode.NotFound, notApproved.Status);
    }

    [Fact]
    public async Task Fassungen_vor_1_2_0_bekommen_keine_Dev_Versionen()
    {
        var admin = await client.SignInAdminAsync();
        await client.CreateUserAsync(admin, "entwickler", level: "Entwickler");

        var current = factory.CreateApp("1.2.0");
        var old = factory.CreateApp("1.1.5");

        var token = await current.SignInAsync("entwickler", "Sehr-sicheres-Passwort-42", "app");

        var fromCurrent = (await current.SendAsync(HttpMethod.Get, "/api/app/update?current=1.2.0&dev=true", token: token)).Body;
        var fromOld = (await old.SendAsync(HttpMethod.Get, "/api/app/update?current=1.1.5&dev=true", token: token)).Body;
        var oldList = (await old.SendAsync(HttpMethod.Get, "/api/app/releases", token: token)).Body;
        var oldDownload = await old.SendAsync(HttpMethod.Get, "/api/app/download/v1.3.0-dev", token: token);

        Assert.Equal("v1.3.0-dev", fromCurrent.GetProperty("update").GetProperty("tag").GetString());
        Assert.Equal("v1.2.0", fromOld.GetProperty("update").GetProperty("tag").GetString());
        Assert.DoesNotContain(oldList.EnumerateArray(), r => r.GetProperty("isDev").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, oldDownload.Status);
    }

    [Fact]
    public async Task Öffentliche_Version_lässt_sich_nicht_freigeben_und_Releases_brauchen_eine_höhere_Nummer()
    {
        var admin = await client.SignInAdminAsync();

        var approval = await client.SendAsync(HttpMethod.Put, "/api/admin/releases/v1.2.0/approval",
            new { forAllDevelopers = true }, admin);
        var release = await client.SendAsync(HttpMethod.Post, "/api/admin/releases",
            new { channel = "public", version = "1.2.0" }, admin);

        Assert.Equal(HttpStatusCode.BadRequest, approval.Status);
        Assert.Equal(HttpStatusCode.BadRequest, release.Status);
    }

    [Fact]
    public async Task Datenbank_wird_gesichert_und_auf_leerer_Platte_zurückgeholt()
    {
        var admin = await client.SignInAdminAsync();
        await CreateMessageAsync(admin, "Muss-die-Sicherung-überleben", ["App"]);

        var maintenance = factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .OfType<DatabaseMaintenance>().Single();
        maintenance.BackupIfChanged();

        Assert.True(File.Exists(factory.BackupPath));

        // Eine neue, leere Platte: Die Arbeitsdatei fehlt, die Sicherung ist da.
        var emptyDisk = Path.Combine(Path.GetDirectoryName(factory.BackupPath)!, "..", "neue-platte", "arbeit.db");
        var storage = new StorageLocations(emptyDisk, factory.BackupPath, Path.GetTempPath());

        DatabaseMaintenance.RestoreIfMissing(storage, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        Assert.True(File.Exists(emptyDisk));

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={emptyDisk};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Messages WHERE Text = 'Muss-die-Sicherung-überleben'";

        Assert.Equal(1L, (long)command.ExecuteScalar()!);
    }
}
