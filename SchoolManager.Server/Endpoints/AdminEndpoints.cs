using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SchoolManager.Server.Data;
using SchoolManager.Server.GitHub;
using SchoolManager.Server.Security;
using SchoolManager.Server.Services;

namespace SchoolManager.Server.Endpoints;

/// <summary>Meldungen, Verlauf, Einstellungen und was das Panel zum Aufbau braucht.</summary>
public static partial class AdminEndpoints
{
    public sealed record MessageInput(
        string? Text, MessageKind Kind, MessageAudience Audience, bool IsActive, DateTimeOffset? ExpiresAt, string[]? Placements, string? AppPage);

    public sealed record SettingsInput(string? DevBranch);

    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/meta", MetaAsync).RequireSignIn();

        // Namen von Benutzern und Gruppen - für Freigaben, ohne gleich alle Benutzerdaten zu zeigen.
        app.MapGet("/api/admin/directory", async (ServerDb db) => Results.Ok(new
        {
            users = await db.Users.OrderBy(u => u.UserName)
                .Select(u => new { id = u.Id, userName = u.UserName, displayName = u.DisplayName }).ToListAsync(),
            groups = await db.Groups.OrderBy(g => g.Name)
                .Select(g => new { id = g.Id, name = g.Name }).ToListAsync()
        })).RequireAnyPermission(Permission.ApproveUpdates | Permission.Users | Permission.Groups);

        var messages = app.MapGroup("/api/admin/messages");
        messages.MapGet("", ListMessagesAsync).RequirePermission(Permission.Messages);
        messages.MapPost("", CreateMessageAsync).RequirePermission(Permission.Messages);
        messages.MapPut("/{id:int}", UpdateMessageAsync).RequirePermission(Permission.Messages);
        messages.MapDelete("/{id:int}", DeleteMessageAsync).RequirePermission(Permission.Messages);

        app.MapGet("/api/admin/audit", async (ServerDb db, int? take) => Results.Ok(
            (await db.AuditEntries.OrderByDescending(a => a.Time).Take(Math.Clamp(take ?? 300, 1, 1000)).ToListAsync())
            .Select(a => new { time = a.Time, actor = a.Actor, action = a.Action })))
            .RequirePermission(Permission.Audit);

        app.MapGet("/api/admin/settings", async (ServerSettings settings, GitHubService github) => Results.Ok(new
        {
            devBranch = await settings.DevBranchAsync(),
            publicBranch = github.PublicBranch,
            owner = github.Owner,
            publicRepo = github.PublicRepo,
            devRepo = github.DevRepo,
            hasToken = github.HasToken
        })).RequirePermission(Permission.Settings);

        app.MapPut("/api/admin/settings", UpdateSettingsAsync).RequirePermission(Permission.Settings);
    }

    private static IResult MetaAsync(HttpContext context) => Results.Ok(new
    {
        me = AuthEndpoints.Me(CurrentUser.Of(context)!),
        permissions = Permissions.Catalog.Select(p => new { name = p.Value.ToString(), label = p.Label, hint = p.Hint }),
        levels = Enum.GetValues<AccessLevel>().Select(level => new
        {
            name = level.ToString(),
            permissions = Permissions.ToNames(Permissions.ForLevel(level))
        }),
        minimumPasswordLength = PasswordHasher.MinimumLength,
        appPages = AppPages.All.Select(page => new { key = page.Key, label = page.Label })
    });

    private static async Task<IResult> ListMessagesAsync(ServerDb db) =>
        Results.Ok((await db.Messages.ToListAsync())
            .OrderByDescending(m => m.UpdatedAt)
            .Select(m => new
            {
                id = m.Id,
                text = m.Text,
                kind = m.Kind,
                audience = m.Audience,
                placements = PlacementNames(m.Placement),
                appPage = m.AppPage,
                isActive = m.IsActive,
                expiresAt = m.ExpiresAt,
                createdAt = m.CreatedAt,
                updatedAt = m.UpdatedAt,
                updatedBy = m.UpdatedBy
            }));

    private static async Task<IResult> CreateMessageAsync(
        MessageInput input, HttpContext context, ServerDb db, AuditLog audit, TimeProvider clock)
    {
        if (ValidateMessage(input) is { } problem)
            return problem;

        var actor = CurrentUser.Of(context)!.User.UserName;
        var now = clock.GetUtcNow();
        var message = new Message { CreatedAt = now };

        Apply(message, input, actor, now);
        db.Messages.Add(message);
        audit.Add(actor, $"Meldung angelegt: „{Shorten(message.Text)}“");
        await db.SaveChangesAsync();

        return Results.Ok(new { id = message.Id });
    }

    private static async Task<IResult> UpdateMessageAsync(
        int id, MessageInput input, HttpContext context, ServerDb db, AuditLog audit, TimeProvider clock)
    {
        if (ValidateMessage(input) is { } problem)
            return problem;

        if (await db.Messages.FindAsync(id) is not { } message)
            return AuthEndpoints.Error(StatusCodes.Status404NotFound, "Diese Meldung gibt es nicht mehr.");

        var actor = CurrentUser.Of(context)!.User.UserName;

        Apply(message, input, actor, clock.GetUtcNow());
        audit.Add(actor, $"Meldung {(message.IsActive ? "geändert" : "abgeschaltet")}: „{Shorten(message.Text)}“");
        await db.SaveChangesAsync();

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteMessageAsync(int id, HttpContext context, ServerDb db, AuditLog audit)
    {
        if (await db.Messages.FindAsync(id) is not { } message)
            return Results.NoContent();

        db.Messages.Remove(message);
        audit.Add(CurrentUser.Of(context)!.User.UserName, $"Meldung gelöscht: „{Shorten(message.Text)}“");
        await db.SaveChangesAsync();

        return Results.NoContent();
    }

    private static IResult? ValidateMessage(MessageInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Text))
            return AuthEndpoints.Error(StatusCodes.Status400BadRequest, "Die Meldung braucht einen Text.");

        if (input.Text.Trim().Length > 1000)
            return AuthEndpoints.Error(StatusCodes.Status400BadRequest, "Die Meldung darf höchstens 1000 Zeichen lang sein.");

        if (!Enum.IsDefined(input.Kind) || !Enum.IsDefined(input.Audience))
            return AuthEndpoints.Error(StatusCodes.Status400BadRequest, "Art oder Empfänger der Meldung sind ungültig.");

        if (!AppPages.IsKnown(input.AppPage))
            return AuthEndpoints.Error(StatusCodes.Status400BadRequest, "Diese Seite der App gibt es nicht.");

        if (ParsePlacements(input.Placements) == MessagePlacement.None)
            return AuthEndpoints.Error(StatusCodes.Status400BadRequest, "Die Meldung braucht mindestens einen Ort, an dem sie erscheint.");

        return null;
    }

    /// <summary>Ohne Angabe gilt die App - so wie bei jeder Meldung, bevor es Orte gab.</summary>
    private static MessagePlacement ParsePlacements(string[]? names)
    {
        if (names is null)
            return MessagePlacement.App;

        var result = MessagePlacement.None;

        foreach (var name in names)
        {
            if (Enum.TryParse<MessagePlacement>(name, ignoreCase: false, out var value)
                && value is MessagePlacement.App or MessagePlacement.StartPage or MessagePlacement.SignInPage)
                result |= value;
        }

        return result;
    }

    private static string[] PlacementNames(MessagePlacement placement) =>
        new[] { MessagePlacement.App, MessagePlacement.StartPage, MessagePlacement.SignInPage }
            .Where(p => placement.HasFlag(p))
            .Select(p => p.ToString())
            .ToArray();

    private static void Apply(Message message, MessageInput input, string actor, DateTimeOffset now)
    {
        message.Text = input.Text!.Trim();
        message.Kind = input.Kind;
        message.Audience = input.Audience;
        message.Placement = ParsePlacements(input.Placements);

        // Eine Seite der App zählt nur, wenn die Meldung überhaupt in der App erscheint.
        message.AppPage = message.Placement.HasFlag(MessagePlacement.App) ? input.AppPage ?? AppPages.Everywhere : AppPages.Everywhere;
        message.IsActive = input.IsActive;
        message.ExpiresAt = input.ExpiresAt;
        message.UpdatedAt = now;
        message.UpdatedBy = actor;
    }

    private static async Task<IResult> UpdateSettingsAsync(
        SettingsInput input, HttpContext context, ServerDb db, ServerSettings settings, AuditLog audit)
    {
        var branch = (input.DevBranch ?? "").Trim();

        if (!BranchName().IsMatch(branch) || branch.Contains(".."))
            return AuthEndpoints.Error(StatusCodes.Status400BadRequest, "Das ist kein gültiger Zweigname.");

        await settings.SetDevBranchAsync(branch);
        audit.Add(CurrentUser.Of(context)!.User.UserName, $"Entwicklungszweig auf „{branch}“ gesetzt.");
        await db.SaveChangesAsync();

        return Results.NoContent();
    }

    public static string Shorten(string text) => text.Length > 80 ? text[..80] + "…" : text;

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._/-]{0,99}$")]
    private static partial Regex BranchName();
}
