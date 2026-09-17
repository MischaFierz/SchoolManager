using Microsoft.EntityFrameworkCore;
using SchoolManager.Server.Data;
using SchoolManager.Server.GitHub;
using SchoolManager.Server.Security;
using SchoolManager.Server.Services;

namespace SchoolManager.Server.Endpoints;

/// <summary>
/// Was School Manager selbst abfragt. Meldungen und die Update-Suche gehen auch
/// ohne Anmeldung; wer als Entwickler angemeldet ist, bekommt dazu seine
/// Meldungen und Dev-Versionen.
/// </summary>
public static class AppEndpoints
{
    public static void MapAppEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/app/messages", MessagesAsync);
        app.MapGet("/api/app/update", UpdateAsync);

        // Für den Download-Bereich der Startseite.
        app.MapGet("/api/public/latest", LatestPublicAsync);

        app.MapGet("/api/app/releases", ReleasesAsync).RequirePermission(Permission.DevMode);
        app.MapGet("/api/app/download/{tag}", DownloadAsync).RequirePermission(Permission.DevMode);
    }

    private static async Task<IResult> MessagesAsync(HttpContext context, ServerDb db, TimeProvider clock)
    {
        var user = CurrentUser.Of(context);
        var developer = user is not null && user.Permissions.Has(Permission.DevMode);
        var now = clock.GetUtcNow();

        var messages = await db.Messages
            .Where(m => m.IsActive)
            .Where(m => m.Audience == MessageAudience.Everyone || developer)
            .ToListAsync();

        return Results.Ok(messages
            .Where(m => m.ExpiresAt is null || m.ExpiresAt > now)
            .OrderBy(m => m.Kind)
            .ThenByDescending(m => m.UpdatedAt)
            .Select(m => new
            {
                id = m.Id,
                text = m.Text,
                kind = m.Kind,
                // Ändert sich der Text, erscheint eine weggeklickte Meldung wieder.
                revision = m.UpdatedAt.UtcTicks
            }));
    }

    /// <param name="current">Die laufende Version, etwa 1.2.0.</param>
    /// <param name="dev">Der Entwicklermodus will Dev-Versionen sehen.</param>
    private static async Task<IResult> UpdateAsync(
        string? current, bool? dev, HttpContext context, ReleaseCatalog catalog)
    {
        if (!VersionTag.TryParseNumber(current, out var running))
            return AuthEndpoints.Error(StatusCodes.Status400BadRequest, "Die laufende Version fehlt oder ist unlesbar.");

        var user = CurrentUser.Of(context);
        var allowed = await catalog.AllowedAsync(user, dev == true);

        // Bei gleicher Nummer steht das öffentliche Release vorne - es ist das fertige.
        var newest = allowed.FirstOrDefault(r => r.Version > running);

        if (newest is null)
            return Results.Ok(new { update = (object?)null });

        var notes = await catalog.NotesAsync();

        return Results.Ok(new { update = Describe(newest, notes) });
    }

    /// <summary>Die neueste öffentliche Version mit beiden Dateien - nie eine Dev-Version.</summary>
    private static async Task<IResult> LatestPublicAsync(ReleaseCatalog catalog)
    {
        var latest = (await catalog.AllowedAsync(user: null, includeDev: false)).FirstOrDefault();

        if (latest?.Installer is not { } installer)
            return AuthEndpoints.Error(StatusCodes.Status404NotFound, "Es ist noch keine Version veröffentlicht.");

        var exe = latest.Source.Assets.FirstOrDefault(asset =>
            asset.Name.Equals(GitHubService.ExeAssetName, StringComparison.OrdinalIgnoreCase));

        var notes = await catalog.NotesAsync();

        return Results.Ok(new
        {
            version = latest.VersionText,
            publishedAt = latest.Source.PublishedAt,
            releaseUrl = latest.Source.HtmlUrl,
            note = notes.GetValueOrDefault(latest.Tag, ""),
            installer = new { url = installer.BrowserDownloadUrl, size = installer.Size },
            exe = exe is null ? null : new { url = exe.BrowserDownloadUrl, size = exe.Size }
        });
    }

    private static async Task<IResult> ReleasesAsync(HttpContext context, ReleaseCatalog catalog)
    {
        var allowed = await catalog.AllowedAsync(CurrentUser.Of(context), includeDev: true);
        var notes = await catalog.NotesAsync();

        return Results.Ok(allowed.Select(r => Describe(r, notes)));
    }

    /// <summary>
    /// Die Download-Adresse einer Version - aber nur, wenn dieser Benutzer sie
    /// beziehen darf. Für Dev-Versionen ist es eine signierte Adresse, die nach
    /// wenigen Minuten verfällt.
    /// </summary>
    private static async Task<IResult> DownloadAsync(
        string tag, HttpContext context, ReleaseCatalog catalog, GitHubService github)
    {
        var allowed = await catalog.AllowedAsync(CurrentUser.Of(context), includeDev: true);
        var release = allowed.FirstOrDefault(r => r.Tag == tag);

        if (release?.Installer is not { } installer)
            return AuthEndpoints.Error(StatusCodes.Status404NotFound, "Diese Version ist für dieses Konto nicht freigegeben.");

        if (release.InPublicRepo)
            return Results.Ok(new { url = installer.BrowserDownloadUrl });

        try
        {
            return Results.Ok(new { url = await github.SignedDownloadUrlAsync(release.Repo, installer.Id) });
        }
        catch (GitHubException ex)
        {
            return AuthEndpoints.Error(StatusCodes.Status502BadGateway, ex.Message);
        }
    }

    private static object Describe(CatalogRelease release, IReadOnlyDictionary<string, string> notes) => new
    {
        version = release.VersionText,
        tag = release.Tag,
        isDev = release.IsDev,
        size = release.Installer?.Size ?? 0,
        releaseUrl = release.InPublicRepo ? release.Source.HtmlUrl : "",
        // Dev-Versionen holt die App sich einzeln über /api/app/download.
        downloadUrl = release.InPublicRepo ? release.Installer?.BrowserDownloadUrl ?? "" : "",
        note = notes.GetValueOrDefault(release.Tag, "")
    };
}
