using Microsoft.EntityFrameworkCore;
using SchoolManager.Server.Data;
using SchoolManager.Server.GitHub;
using SchoolManager.Server.Security;
using SchoolManager.Server.Services;

namespace SchoolManager.Server.Endpoints;

/// <summary>
/// Versionen im Panel: Hinweistexte, Freigaben, Veröffentlichen und Aufräumen.
///
/// Veröffentlicht wird, wie es die Release-Notizen verlangen - nur über einen
/// Tag, den der Workflow des jeweiligen Repositorys baut. Dev-Versionen werden
/// ausschliesslich im privaten Repository getaggt, öffentliche Releases nur auf
/// dem öffentlichen Hauptzweig. Code verschiebt das Panel nie.
/// </summary>
public static class ReleaseEndpoints
{
    public sealed record NoteInput(string? Text);

    public sealed record ApprovalInput(bool ForAllDevelopers, int[]? UserIds, int[]? GroupIds);

    public sealed record ReleaseInput(string? Channel, string? Version, bool ConfirmVersionMismatch, bool ApproveForAllDevelopers);

    public static void MapReleaseEndpoints(this IEndpointRouteBuilder app)
    {
        var releases = app.MapGroup("/api/admin/releases");

        releases.MapGet("", ListAsync)
            .RequireAnyPermission(Permission.UpdateNotes | Permission.ApproveUpdates | Permission.Release);
        releases.MapPut("/{tag}/note", SetNoteAsync).RequirePermission(Permission.UpdateNotes);
        releases.MapPut("/{tag}/approval", SetApprovalAsync).RequirePermission(Permission.ApproveUpdates);
        releases.MapGet("/prepare", PrepareAsync).RequirePermission(Permission.Release);
        releases.MapPost("", PublishAsync).RequirePermission(Permission.Release);
        releases.MapGet("/{tag}/cleanup", CleanupPreviewAsync).RequirePermission(Permission.Release);
        releases.MapPost("/{tag}/cleanup", CleanupAsync).RequirePermission(Permission.Release);
    }

    private static async Task<IResult> ListAsync(
        ReleaseCatalog catalog, GitHubService github, ServerDb db, ServerSettings settings)
    {
        var all = await catalog.AllAsync();
        var notes = await db.ReleaseNotes.ToDictionaryAsync(n => n.Tag);
        var approvals = await db.DevApprovals.Include(a => a.Users).Include(a => a.Groups).ToDictionaryAsync(a => a.Tag);
        var runs = (await github.RunsAsync(github.PublicRepo)).Select(r => (Repo: github.PublicRepo, Run: r))
            .Concat((await github.RunsAsync(github.DevRepo)).Select(r => (Repo: github.DevRepo, Run: r)))
            .Where(r => r.Run.HeadBranch is not null)
            .ToList();

        object? Build(string repo, string tag) => runs
            .Where(r => r.Repo == repo && r.Run.HeadBranch == tag)
            .OrderByDescending(r => r.Run.CreatedAt)
            .Select(r => new { status = r.Run.Status, conclusion = r.Run.Conclusion, url = r.Run.HtmlUrl })
            .FirstOrDefault();

        // Bauläufe für Tags, zu denen es noch kein Release gibt - frisch getaggt oder gescheitert.
        var building = runs
            .Where(r => VersionTag.TryParse(r.Run.HeadBranch, out _, out _))
            .Where(r => all.All(release => release.Tag != r.Run.HeadBranch || release.Repo != r.Repo))
            .GroupBy(r => (r.Repo, r.Run.HeadBranch))
            .Select(g => g.OrderByDescending(r => r.Run.CreatedAt).First())
            .Select(r => new
            {
                tag = r.Run.HeadBranch,
                repo = r.Repo,
                status = r.Run.Status,
                conclusion = r.Run.Conclusion,
                url = r.Run.HtmlUrl,
                createdAt = r.Run.CreatedAt
            });

        return Results.Ok(new
        {
            hasToken = github.HasToken,
            publicRepo = github.PublicRepo,
            devRepo = github.DevRepo,
            devBranch = await settings.DevBranchAsync(),
            building,
            releases = all.Select(r => new
            {
                tag = r.Tag,
                version = r.VersionText,
                repo = r.Repo,
                isDev = r.IsDev,
                inPublicRepo = r.InPublicRepo,
                isLegacyDev = r.IsLegacyDev,
                publishedAt = r.Source.PublishedAt,
                htmlUrl = r.Source.HtmlUrl,
                hasInstaller = r.Installer is not null,
                hasExe = r.HasExe,
                size = r.Installer?.Size ?? 0,
                note = notes.GetValueOrDefault(r.Tag)?.Text ?? "",
                noteUpdatedBy = notes.GetValueOrDefault(r.Tag)?.UpdatedBy,
                approval = approvals.GetValueOrDefault(r.Tag) is { } a
                    ? new
                    {
                        forAllDevelopers = a.ForAllDevelopers,
                        userIds = a.Users.Select(u => u.Id).ToArray(),
                        groupIds = a.Groups.Select(g => g.Id).ToArray(),
                        updatedBy = a.UpdatedBy
                    }
                    : null,
                build = Build(r.Repo, r.Tag)
            })
        });
    }

    private static async Task<IResult> SetNoteAsync(
        string tag, NoteInput input, HttpContext context, ServerDb db, AuditLog audit, TimeProvider clock)
    {
        if (!VersionTag.TryParse(tag, out _, out _))
            return AuthEndpoints.Error(StatusCodes.Status400BadRequest, "Das ist kein Versions-Tag.");

        var text = (input.Text ?? "").Trim();

        if (text.Length > 1000)
            return AuthEndpoints.Error(StatusCodes.Status400BadRequest, "Die Update-Info darf höchstens 1000 Zeichen lang sein.");

        var actor = CurrentUser.Of(context)!.User.UserName;
        var note = await db.ReleaseNotes.FindAsync(tag);

        if (text.Length == 0)
        {
            if (note is not null)
                db.ReleaseNotes.Remove(note);

            audit.Add(actor, $"Update-Info zu {tag} entfernt.");
        }
        else
        {
            if (note is null)
                db.ReleaseNotes.Add(note = new ReleaseNote { Tag = tag });

            note.Text = text;
            note.UpdatedAt = clock.GetUtcNow();
            note.UpdatedBy = actor;
            audit.Add(actor, $"Update-Info zu {tag}: „{AdminEndpoints.Shorten(text)}“");
        }

        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> SetApprovalAsync(
        string tag, ApprovalInput input, HttpContext context, ServerDb db, AuditLog audit, TimeProvider clock)
    {
        if (!VersionTag.TryParse(tag, out _, out var isDev) || !isDev)
            return AuthEndpoints.Error(StatusCodes.Status400BadRequest, "Freigeben lassen sich nur Dev-Versionen.");

        var actor = CurrentUser.Of(context)!.User.UserName;
        var userIds = (input.UserIds ?? []).Distinct().ToList();
        var groupIds = (input.GroupIds ?? []).Distinct().ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToListAsync();
        var groups = await db.Groups.Where(g => groupIds.Contains(g.Id)).ToListAsync();

        var approval = await db.DevApprovals.Include(a => a.Users).Include(a => a.Groups).FirstOrDefaultAsync(a => a.Tag == tag);

        if (!input.ForAllDevelopers && users.Count == 0 && groups.Count == 0)
        {
            if (approval is not null)
                db.DevApprovals.Remove(approval);

            audit.Add(actor, $"Freigabe von {tag} zurückgezogen.");
        }
        else
        {
            if (approval is null)
                db.DevApprovals.Add(approval = new DevApproval { Tag = tag });

            approval.ForAllDevelopers = input.ForAllDevelopers;
            approval.Users = users;
            approval.Groups = groups;
            approval.UpdatedAt = clock.GetUtcNow();
            approval.UpdatedBy = actor;

            var who = input.ForAllDevelopers
                ? "alle Entwickler"
                : string.Join(", ", users.Select(u => u.UserName).Concat(groups.Select(g => "Gruppe " + g.Name)));
            audit.Add(actor, $"{tag} freigegeben für: {who}.");
        }

        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> PrepareAsync(ReleaseCatalog catalog, GitHubService github, ServerSettings settings)
    {
        var all = await catalog.AllAsync();
        var newestPublic = NewestPublic(all);
        var newestDev = NewestDev(all);
        var devBranch = await settings.DevBranchAsync();

        return Results.Ok(new
        {
            hasToken = github.HasToken,
            newestPublic = newestPublic?.ToString(3),
            newestDev = newestDev?.ToString(3),
            dev = await DescribeBranchAsync(github, github.DevRepo, devBranch,
                Max(newestPublic, newestDev)),
            @public = await DescribeBranchAsync(github, github.PublicRepo, github.PublicBranch,
                newestPublic)
        });
    }

    private static async Task<object> DescribeBranchAsync(GitHubService github, string repo, string branch, Version? mustExceed)
    {
        try
        {
            var head = await github.BranchHeadAsync(repo, branch);
            var projectVersion = await github.ProjectVersionAsync(repo, branch);

            VersionTag.TryParseNumber(projectVersion, out var project);

            var suggested = mustExceed is null || project > mustExceed
                ? project
                : new Version(mustExceed.Major, mustExceed.Minor, mustExceed.Build + 1);

            return new
            {
                repo,
                branch,
                commit = new { sha = head.Sha, message = head.Message, date = head.Date },
                projectVersion,
                suggestedVersion = suggested.ToString(3),
                error = (string?)null
            };
        }
        catch (GitHubException ex)
        {
            return new { repo, branch, commit = (object?)null, projectVersion = (string?)null, suggestedVersion = (string?)null, error = ex.Message };
        }
    }

    private static async Task<IResult> PublishAsync(
        ReleaseInput input, HttpContext context, ReleaseCatalog catalog, GitHubService github,
        ServerSettings settings, ServerDb db, AuditLog audit, TimeProvider clock)
    {
        var actor = CurrentUser.Of(context)!;
        var dev = input.Channel == "dev";

        if (!dev && input.Channel != "public")
            return AuthEndpoints.Error(StatusCodes.Status400BadRequest, "Unbekannter Kanal - dev oder public.");

        if (!VersionTag.TryParseNumber(input.Version, out var version))
            return AuthEndpoints.Error(StatusCodes.Status400BadRequest, "Die Version muss die Form 1.2.3 haben.");

        var all = await catalog.AllAsync();
        var newestPublic = NewestPublic(all);
        var newestDev = NewestDev(all);

        // Die Nummern zählen in beiden Kanälen gemeinsam weiter: Eine Dev-Version
        // muss neuer sein als alles bisher, ein Release neuer als das letzte Release.
        if (dev && Max(newestPublic, newestDev) is { } highest && version <= highest)
            return AuthEndpoints.Error(StatusCodes.Status400BadRequest,
                $"Eine Dev-Version braucht eine höhere Nummer als {highest.ToString(3)} - sonst bietet die Update-Suche sie niemandem an.");

        if (!dev && newestPublic is not null && version <= newestPublic)
            return AuthEndpoints.Error(StatusCodes.Status400BadRequest,
                $"Ein Release braucht eine höhere Nummer als {newestPublic.ToString(3)} - sonst führt GitHub die kleinere als neueste.");

        var repo = dev ? github.DevRepo : github.PublicRepo;
        var branch = dev ? await settings.DevBranchAsync() : github.PublicBranch;
        var tag = $"v{version.ToString(3)}{(dev ? "-dev" : "")}";

        if (await github.TagExistsAsync(repo, tag))
            return AuthEndpoints.Error(StatusCodes.Status400BadRequest, $"Den Tag {tag} gibt es in {repo} schon.");

        var head = await github.BranchHeadAsync(repo, branch);
        var projectVersion = await github.ProjectVersionAsync(repo, branch);

        if (projectVersion != version.ToString(3) && !input.ConfirmVersionMismatch)
            return Results.Json(new
            {
                error = $"In der Projektdatei auf {branch} steht die Version {projectVersion ?? "(keine)"}, nicht {version.ToString(3)}. "
                        + "Die Dateien trügen dann die Nummer aus dem Tag, eine selbst gebaute Fassung aber eine andere.",
                code = "version-mismatch",
                projectVersion
            }, statusCode: StatusCodes.Status409Conflict);

        await github.CreateTagAsync(repo, tag, head.Sha);

        audit.Add(actor.User.UserName,
            $"{(dev ? "Dev-Version" : "Öffentliches Release")} {tag} ausgelöst auf {repo}/{branch} ({head.Sha[..7]} „{AdminEndpoints.Shorten(head.Message)}“).");

        if (dev && input.ApproveForAllDevelopers && actor.Permissions.Has(Permission.ApproveUpdates))
        {
            var approval = await db.DevApprovals.FindAsync(tag);

            if (approval is null)
                db.DevApprovals.Add(approval = new DevApproval { Tag = tag });

            approval.ForAllDevelopers = true;
            approval.UpdatedAt = clock.GetUtcNow();
            approval.UpdatedBy = actor.User.UserName;
            audit.Add(actor.User.UserName, $"{tag} freigegeben für: alle Entwickler.");
        }

        await db.SaveChangesAsync();

        return Results.Ok(new { tag, repo, commit = head.Sha, actionsUrl = github.ActionsUrl(repo) });
    }

    private static async Task<IResult> CleanupPreviewAsync(string tag, ReleaseCatalog catalog)
    {
        var (release, problem, candidates) = await FindCleanupAsync(tag, catalog);

        return Results.Ok(new
        {
            ready = release is not null && problem is null,
            problem,
            candidates = candidates.Select(c => new { tag = c.Tag, repo = c.Repo })
        });
    }

    /// <summary>
    /// Löscht die Dev-Versionen, die in einem öffentlichen Release aufgegangen
    /// sind - erst, wenn dieses Release mit beiden Dateien steht. Scheitert der
    /// Bau, bleibt der Dev-Kanal so nicht leer zurück.
    /// </summary>
    private static async Task<IResult> CleanupAsync(
        string tag, HttpContext context, ReleaseCatalog catalog, GitHubService github, ServerDb db, AuditLog audit)
    {
        var (release, problem, candidates) = await FindCleanupAsync(tag, catalog);

        if (release is null || problem is not null)
            return AuthEndpoints.Error(StatusCodes.Status400BadRequest, problem ?? "Dieses Release gibt es nicht.");

        var actor = CurrentUser.Of(context)!.User.UserName;
        var deleted = new List<string>();

        try
        {
            foreach (var candidate in candidates)
            {
                await github.DeleteReleaseAsync(candidate.Repo, candidate.Source);
                deleted.Add(candidate.Tag);
            }
        }
        finally
        {
            // Auch nach einem Abbruch festhalten, was schon weg ist.
            await db.DevApprovals.Where(a => deleted.Contains(a.Tag)).ExecuteDeleteAsync();
            await db.ReleaseNotes.Where(n => deleted.Contains(n.Tag)).ExecuteDeleteAsync();

            if (deleted.Count > 0)
            {
                audit.Add(actor, $"Nach {tag} aufgeräumt, gelöscht: {string.Join(", ", deleted)}.");
                await db.SaveChangesAsync();
            }
        }

        return Results.Ok(new { deleted });
    }

    private static async Task<(CatalogRelease? Release, string? Problem, IReadOnlyList<CatalogRelease> Candidates)> FindCleanupAsync(
        string tag, ReleaseCatalog catalog)
    {
        var all = await catalog.AllAsync();
        var release = all.FirstOrDefault(r => r.Tag == tag && r.InPublicRepo && !r.IsLegacyDev);

        if (release is null)
            return (null, "Aufgeräumt wird nur nach einem öffentlichen Release.", []);

        var candidates = all
            .Where(r => (!r.InPublicRepo || r.IsLegacyDev) && r.Version <= release.Version)
            .ToList();

        if (!release.IsComplete)
            return (release, $"{tag} hat noch nicht beide Dateien - erst aufräumen, wenn der Bau grün ist.", candidates);

        return (release, null, candidates);
    }

    private static Version? NewestPublic(IEnumerable<CatalogRelease> all) =>
        all.Where(r => r.InPublicRepo && !r.IsLegacyDev).Select(r => r.Version).DefaultIfEmpty().Max();

    private static Version? NewestDev(IEnumerable<CatalogRelease> all) =>
        all.Where(r => !r.InPublicRepo).Select(r => r.Version).DefaultIfEmpty().Max();

    private static Version? Max(Version? a, Version? b) => a is null ? b : b is null ? a : a > b ? a : b;
}
