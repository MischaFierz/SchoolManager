using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SchoolManager.Server.GitHub;

namespace SchoolManager.Server.Tests;

/// <summary>
/// Ein Server nur für die Tests: eigene, leere Datenbank in einem Temp-Ordner,
/// ein bekannter Administrator, keine Anmeldebremse und ein nachgestelltes
/// GitHub - die Tests brauchen weder Netz noch Token.
/// </summary>
public sealed class ServerFactory : WebApplicationFactory<Program>
{
    public const string AdminPassword = "Test-Admin-Passwort-42";

    private readonly string folder = Path.Combine(Path.GetTempPath(), "schoolmanager-tests", Guid.NewGuid().ToString("N"));

    public string DatabasePath => Path.Combine(folder, "arbeit.db");

    public string BackupPath => Path.Combine(folder, "dauerhaft", "schoolmanager.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("Storage:Database", DatabasePath);
        builder.UseSetting("Storage:Backup", BackupPath);
        builder.UseSetting("Admin:UserName", "admin");
        builder.UseSetting("Admin:Password", AdminPassword);
        builder.UseSetting("Security:AttemptsPerMinute", "100000");
        builder.UseSetting("GitHub:Token", "nur-fuer-tests");

        builder.ConfigureTestServices(services =>
            services.AddHttpClient(nameof(GitHubService))
                .ConfigurePrimaryHttpMessageHandler(() => new FakeGitHub()));
    }

    /// <summary>Ein Client, der sich wie School Manager in der angegebenen Version ausweist.</summary>
    public HttpClient CreateApp(string version = "1.2.0")
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SchoolManager", version));
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // Liegt im Temp-Ordner; Windows räumt ihn irgendwann selbst.
        }
    }
}

/// <summary>
/// Nachgestelltes GitHub: im öffentlichen Repository v1.1.5 und v1.2.0, im
/// privaten die Dev-Versionen v1.2.1-dev und v1.3.0-dev.
/// </summary>
public sealed class FakeGitHub : HttpMessageHandler
{
    private static object Release(long id, string tag, bool prerelease, string repo) => new
    {
        id,
        tag_name = tag,
        draft = false,
        prerelease,
        html_url = $"https://github.com/MischaFierz/{repo}/releases/tag/{tag}",
        published_at = "2026-09-17T12:00:00Z",
        assets = new object[]
        {
            new { id = id * 10 + 1, name = "SchoolManagerSetup.msi", size = 1000, browser_download_url = $"https://github.com/MischaFierz/{repo}/releases/download/{tag}/SchoolManagerSetup.msi" },
            new { id = id * 10 + 2, name = "SchoolManager.exe", size = 2000, browser_download_url = $"https://github.com/MischaFierz/{repo}/releases/download/{tag}/SchoolManager.exe" }
        }
    };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (path.EndsWith("/SchoolManager/releases"))
            return Json(new[] { Release(2, "v1.2.0", false, "SchoolManager"), Release(1, "v1.1.5", false, "SchoolManager") });

        if (path.EndsWith("/SchoolManager-dev/releases"))
            return Json(new[] { Release(4, "v1.3.0-dev", true, "SchoolManager-dev"), Release(3, "v1.2.1-dev", true, "SchoolManager-dev") });

        if (path.Contains("/releases/assets/"))
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri("https://objects.githubusercontent.com/signiert?kurzlebig=1");
            return Task.FromResult(redirect);
        }

        if (path.Contains("/actions/runs"))
            return Json(new { workflow_runs = Array.Empty<object>() });

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static Task<HttpResponseMessage> Json(object body) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        });
}

/// <summary>Kurze Helfer für Anfragen mit Token.</summary>
public static class Api
{
    public static async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(
        this HttpClient client, HttpMethod method, string path, object? body = null, string? token = null)
    {
        using var request = new HttpRequestMessage(method, path);

        if (body is not null)
            request.Content = JsonContent.Create(body);

        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();

        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    public static async Task<string> SignInAsync(this HttpClient client, string userName, string password, string kind = "panel")
    {
        var (status, body) = await client.SendAsync(HttpMethod.Post, "/api/auth/login", new { userName, password, client = kind });
        Assert.Equal(HttpStatusCode.OK, status);
        return body.GetProperty("token").GetString()!;
    }

    public static Task<string> SignInAdminAsync(this HttpClient client) =>
        client.SignInAsync("admin", ServerFactory.AdminPassword);

    /// <summary>Legt einen Benutzer an und gibt Id und Passwort zurück.</summary>
    public static async Task<(int Id, string Password)> CreateUserAsync(
        this HttpClient client, string adminToken, string userName, string level = "Tester",
        string[]? extra = null, int[]? groups = null, bool publicOnly = false)
    {
        const string password = "Sehr-sicheres-Passwort-42";

        var (status, body) = await client.SendAsync(HttpMethod.Post, "/api/admin/users", new
        {
            userName,
            displayName = userName,
            level,
            extraPermissions = extra ?? [],
            groupIds = groups ?? [],
            publicOnly,
            isActive = true,
            password,
            generatePassword = false,
            mustChangePassword = false
        }, adminToken);

        Assert.Equal(HttpStatusCode.OK, status);
        return (body.GetProperty("id").GetInt32(), password);
    }
}
