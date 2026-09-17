using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using SchoolManager.Server.Data;
using SchoolManager.Server.Endpoints;
using SchoolManager.Server.GitHub;
using SchoolManager.Server.Security;
using SchoolManager.Server.Services;

var builder = WebApplication.CreateBuilder(args);

var databasePath = Path.GetFullPath(
    builder.Configuration["Storage:Database"] ?? Path.Combine("App_Data", "schoolmanager.db"),
    builder.Environment.ContentRootPath);

Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

builder.Services.AddDbContext<ServerDb>(options => options.UseSqlite($"Data Source={databasePath}"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache();
builder.Services.Configure<GitHubOptions>(builder.Configuration.GetSection("GitHub"));

// Keine Umleitungen folgen: Für den Download einer Dev-Version braucht es genau die Umleitungsadresse.
builder.Services.AddHttpClient(nameof(GitHubService), client => client.Timeout = TimeSpan.FromSeconds(30))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

builder.Services.AddSingleton<GitHubService>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<AuditLog>();
builder.Services.AddScoped<ReleaseCatalog>();
builder.Services.AddScoped<ServerSettings>();

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Gegen das Durchprobieren von Passwörtern: je Internetadresse 10 Versuche pro Minute.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, token) => new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(
        new { error = "Zu viele Versuche in kurzer Zeit. Bitte eine Minute warten." }, token));

    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unbekannt",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));

    // Eigene Bremse fürs Passwortändern, damit sie nicht die Anmeldungen im selben Netz aufbraucht.
    options.AddPolicy("password", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unbekannt",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

if (builder.Configuration.GetValue<bool>("Server:BehindProxy"))
{
    // Hinter Azure, nginx oder Caddy kommt die echte Adresse im Kopf X-Forwarded-For.
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ServerDb>();
    await db.Database.MigrateAsync();

    // Notausgang, wenn niemand mehr ins Panel kommt:
    //   dotnet SchoolManager.Server.dll --passwort-zuruecksetzen admin
    if (args.Length >= 2 && args[0] == "--passwort-zuruecksetzen")
    {
        await AdminSeeder.ResetPasswordAsync(db, args[1]);
        return;
    }

    await AdminSeeder.EnsureAdministratorAsync(db, app.Configuration, app.Logger);
}

if (app.Configuration.GetValue<bool>("Server:BehindProxy"))
    app.UseForwardedHeaders();

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    headers.XContentTypeOptions = "nosniff";
    headers["Referrer-Policy"] = "no-referrer";
    headers.XFrameOptions = "DENY";

    // Antworten mit Benutzerdaten gehören in keinen Zwischenspeicher.
    if (context.Request.Path.StartsWithSegments("/api"))
        headers.CacheControl = "no-store";

    await next();
});

// Fehler bei GitHub sind keine Serverfehler, sondern eine Auskunft fürs Panel.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (GitHubException ex) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status502BadGateway;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (HttpRequestException ex) when (!context.Response.HasStarted)
    {
        app.Logger.LogWarning(ex, "GitHub nicht erreichbar");
        context.Response.StatusCode = StatusCodes.Status502BadGateway;
        await context.Response.WriteAsJsonAsync(new { error = "GitHub ist gerade nicht erreichbar." });
    }
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.Use(CurrentUser.ResolveAsync);

app.MapAuthEndpoints();
app.MapAppEndpoints();
app.MapAdminEndpoints();
app.MapUserEndpoints();
app.MapReleaseEndpoints();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.Run();
