using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SchoolManager.Server.Data;
using SchoolManager.Server.Security;
using SchoolManager.Server.Services;
using Group = SchoolManager.Server.Data.Group;

namespace SchoolManager.Server.Endpoints;

/// <summary>
/// Benutzer und Gruppen. Drei Regeln halten die Rechte zusammen:
/// Niemand vergibt Rechte, die er selbst nicht hat; nur Administratoren
/// verwalten Administratoren; und es bleibt immer ein aktiver Administrator.
/// </summary>
public static partial class UserEndpoints
{
    public sealed record UserInput(
        string? UserName,
        string? DisplayName,
        AccessLevel Level,
        string[]? ExtraPermissions,
        int[]? GroupIds,
        bool PublicOnly,
        bool IsActive,
        string? Password,
        bool GeneratePassword,
        bool MustChangePassword);

    public sealed record PasswordReset(string? Password, bool GeneratePassword, bool MustChangePassword);

    public sealed record GroupInput(string? Name, string? Description, string[]? Permissions);

    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var users = app.MapGroup("/api/admin/users");
        users.MapGet("", ListUsersAsync).RequirePermission(Permission.Users);
        users.MapPost("", CreateUserAsync).RequirePermission(Permission.Users);
        users.MapPut("/{id:int}", UpdateUserAsync).RequirePermission(Permission.Users);
        users.MapDelete("/{id:int}", DeleteUserAsync).RequirePermission(Permission.Users);
        users.MapPost("/{id:int}/password", ResetPasswordAsync).RequirePermission(Permission.Users);
        users.MapPost("/{id:int}/signout", SignOutUserAsync).RequirePermission(Permission.Users);
        users.MapPost("/{id:int}/unlock", UnlockUserAsync).RequirePermission(Permission.Users);

        var groups = app.MapGroup("/api/admin/groups");
        groups.MapGet("", ListGroupsAsync).RequirePermission(Permission.Groups);
        groups.MapPost("", CreateGroupAsync).RequirePermission(Permission.Groups);
        groups.MapPut("/{id:int}", UpdateGroupAsync).RequirePermission(Permission.Groups);
        groups.MapDelete("/{id:int}", DeleteGroupAsync).RequirePermission(Permission.Groups);
    }

    // ==== Benutzer ====

    private static async Task<IResult> ListUsersAsync(ServerDb db, TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        var list = await db.Users.Include(u => u.Groups).Include(u => u.Sessions).OrderBy(u => u.UserName).ToListAsync();

        return Results.Ok(list.Select(u => new
        {
            id = u.Id,
            userName = u.UserName,
            displayName = u.DisplayName,
            level = u.Level,
            extraPermissions = Permissions.ToNames(u.ExtraPermissions),
            effectivePermissions = Permissions.ToNames(CurrentUser.EffectivePermissions(u)),
            groupIds = u.Groups.Select(g => g.Id).ToArray(),
            publicOnly = u.PublicOnly,
            isActive = u.IsActive,
            mustChangePassword = u.MustChangePassword,
            lockedUntil = u.LockedUntil > now ? u.LockedUntil : null,
            createdAt = u.CreatedAt,
            lastLoginAt = u.LastLoginAt,
            appSessions = u.Sessions.Count(s => s.Kind == SessionKind.App && s.ExpiresAt > now),
            panelSessions = u.Sessions.Count(s => s.Kind == SessionKind.Panel && s.ExpiresAt > now)
        }));
    }

    private static async Task<IResult> CreateUserAsync(
        UserInput input, HttpContext context, ServerDb db, AuditLog audit, TimeProvider clock)
    {
        var actor = CurrentUser.Of(context)!;
        var userName = AuthEndpoints.Normalize(input.UserName);

        if (!UserNamePattern().IsMatch(userName))
            return Bad("Der Benutzername braucht 3 bis 32 Zeichen: Kleinbuchstaben, Ziffern, Punkt, Bindestrich oder Unterstrich.");

        if (await db.Users.AnyAsync(u => u.UserName == userName))
            return Bad($"Den Benutzernamen „{userName}“ gibt es schon.");

        var password = input.GeneratePassword ? PasswordHasher.Generate() : input.Password;

        if (PasswordHasher.Weakness(password) is { } weakness)
            return Bad(weakness);

        var user = new User
        {
            UserName = userName,
            CreatedAt = clock.GetUtcNow(),
            PasswordHash = PasswordHasher.Hash(password!),
            MustChangePassword = input.MustChangePassword
        };

        if (await ApplyAsync(user, input, actor, db, isNew: true) is { } problem)
            return problem;

        db.Users.Add(user);
        audit.Add(actor.User.UserName, $"Benutzer „{user.UserName}“ angelegt ({user.Level}).");
        await db.SaveChangesAsync();

        return Results.Ok(new { id = user.Id, generatedPassword = input.GeneratePassword ? password : null });
    }

    private static async Task<IResult> UpdateUserAsync(
        int id, UserInput input, HttpContext context, ServerDb db, SessionService sessions, AuditLog audit)
    {
        var actor = CurrentUser.Of(context)!;

        if (await db.Users.Include(u => u.Groups).FirstOrDefaultAsync(u => u.Id == id) is not { } user)
            return NotFound();

        if (!CanManage(actor, user))
            return Forbidden("Administratoren kann nur ein Administrator ändern.");

        var wasActive = user.IsActive;

        if (await ApplyAsync(user, input, actor, db, isNew: false) is { } problem)
            return problem;

        if (await WouldLoseLastAdministratorAsync(db, user))
            return Bad("Es muss mindestens ein aktiver Administrator bleiben.");

        audit.Add(actor.User.UserName, $"Benutzer „{user.UserName}“ geändert: Stufe {user.Level}, "
                                       + $"{(user.IsActive ? "aktiv" : "gesperrt")}, Gruppen: {string.Join(", ", user.Groups.Select(g => g.Name).DefaultIfEmpty("keine"))}, "
                                       + $"Zusatzrechte: {string.Join(", ", Permissions.ToNames(user.ExtraPermissions).DefaultIfEmpty("keine"))}"
                                       + (user.PublicOnly ? ", nur öffentliche Versionen" : "") + ".");
        await db.SaveChangesAsync();

        // Ein gesperrtes Konto soll sofort überall draussen sein.
        if (wasActive && !user.IsActive)
            await sessions.RevokeAllAsync(user.Id);

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteUserAsync(int id, HttpContext context, ServerDb db, AuditLog audit)
    {
        var actor = CurrentUser.Of(context)!;

        if (await db.Users.FirstOrDefaultAsync(u => u.Id == id) is not { } user)
            return Results.NoContent();

        if (user.Id == actor.User.Id)
            return Bad("Das eigene Konto lässt sich nicht löschen.");

        if (!CanManage(actor, user))
            return Forbidden("Administratoren kann nur ein Administrator löschen.");

        user.IsActive = false;

        if (await WouldLoseLastAdministratorAsync(db, user))
            return Bad("Es muss mindestens ein aktiver Administrator bleiben.");

        db.Users.Remove(user);
        audit.Add(actor.User.UserName, $"Benutzer „{user.UserName}“ gelöscht.");
        await db.SaveChangesAsync();

        return Results.NoContent();
    }

    private static async Task<IResult> ResetPasswordAsync(
        int id, PasswordReset input, HttpContext context, ServerDb db, SessionService sessions, AuditLog audit)
    {
        var actor = CurrentUser.Of(context)!;

        if (await db.Users.FirstOrDefaultAsync(u => u.Id == id) is not { } user)
            return NotFound();

        if (!CanManage(actor, user))
            return Forbidden("Das Passwort eines Administrators kann nur ein Administrator setzen.");

        var password = input.GeneratePassword ? PasswordHasher.Generate() : input.Password;

        if (PasswordHasher.Weakness(password) is { } weakness)
            return Bad(weakness);

        user.PasswordHash = PasswordHasher.Hash(password!);
        user.MustChangePassword = input.MustChangePassword;
        user.FailedLogins = 0;
        user.LockedUntil = null;
        audit.Add(actor.User.UserName, $"Passwort von „{user.UserName}“ neu gesetzt.");
        await db.SaveChangesAsync();

        // Wer das alte Passwort kannte, soll nicht angemeldet bleiben.
        await sessions.RevokeAllAsync(user.Id, exceptSessionId: user.Id == actor.User.Id ? actor.Session.Id : null);

        return Results.Ok(new { generatedPassword = input.GeneratePassword ? password : null });
    }

    private static async Task<IResult> SignOutUserAsync(
        int id, HttpContext context, ServerDb db, SessionService sessions, AuditLog audit)
    {
        var actor = CurrentUser.Of(context)!;

        if (await db.Users.FirstOrDefaultAsync(u => u.Id == id) is not { } user)
            return NotFound();

        if (!CanManage(actor, user))
            return Forbidden("Administratoren kann nur ein Administrator abmelden.");

        await sessions.RevokeAllAsync(user.Id, exceptSessionId: user.Id == actor.User.Id ? actor.Session.Id : null);
        audit.Add(actor.User.UserName, $"„{user.UserName}“ auf allen Geräten abgemeldet.");
        await db.SaveChangesAsync();

        return Results.NoContent();
    }

    private static async Task<IResult> UnlockUserAsync(int id, HttpContext context, ServerDb db, AuditLog audit)
    {
        var actor = CurrentUser.Of(context)!;

        if (await db.Users.FirstOrDefaultAsync(u => u.Id == id) is not { } user)
            return NotFound();

        user.LockedUntil = null;
        user.FailedLogins = 0;
        audit.Add(actor.User.UserName, $"Sperre nach Fehlversuchen für „{user.UserName}“ aufgehoben.");
        await db.SaveChangesAsync();

        return Results.NoContent();
    }

    /// <summary>Übernimmt Stufe, Rechte und Gruppen - nach den Regeln oben.</summary>
    private static async Task<IResult?> ApplyAsync(User user, UserInput input, CurrentUser actor, ServerDb db, bool isNew)
    {
        if (!Enum.IsDefined(input.Level))
            return Bad("Unbekannte Stufe.");

        var displayName = (input.DisplayName ?? "").Trim();

        if (displayName.Length > 100)
            return Bad("Der Anzeigename darf höchstens 100 Zeichen lang sein.");

        var self = !isNew && user.Id == actor.User.Id;

        if (self && (input.Level != user.Level || !input.IsActive))
            return Bad("Die eigene Stufe und die eigene Sperre lassen sich nicht ändern.");

        if (input.Level == AccessLevel.Administrator && user.Level != AccessLevel.Administrator && !actor.IsAdministrator)
            return Forbidden("Zum Administrator macht nur ein Administrator.");

        var extra = Permissions.FromNames(input.ExtraPermissions);
        var before = isNew ? Permission.None : Permissions.ForLevel(user.Level) | user.ExtraPermissions;
        var newlyGranted = (Permissions.ForLevel(input.Level) | extra) & ~before;

        if (!actor.Permissions.Has(newlyGranted))
            return Forbidden("Es lassen sich nur Rechte vergeben, die man selbst hat.");

        var wantedIds = (input.GroupIds ?? []).Distinct().ToList();
        var wanted = await db.Groups.Where(g => wantedIds.Contains(g.Id)).ToListAsync();

        if (wanted.Count != wantedIds.Count)
            return Bad("Eine der gewählten Gruppen gibt es nicht mehr.");

        var added = wanted.Where(g => user.Groups.All(existing => existing.Id != g.Id)).ToList();

        if (added.Any(g => !actor.Permissions.Has(g.Permissions)))
            return Forbidden("In eine Gruppe mit Rechten, die man selbst nicht hat, lässt sich niemand aufnehmen.");

        user.DisplayName = displayName;
        user.Level = input.Level;
        user.ExtraPermissions = extra;
        user.PublicOnly = input.PublicOnly;
        user.IsActive = input.IsActive;
        user.Groups.RemoveAll(g => wanted.All(w => w.Id != g.Id));
        user.Groups.AddRange(added);

        return null;
    }

    private static bool CanManage(CurrentUser actor, User target) =>
        target.Level != AccessLevel.Administrator || actor.IsAdministrator;

    /// <summary>Bliebe nach dieser Änderung kein aktiver Administrator übrig?</summary>
    private static async Task<bool> WouldLoseLastAdministratorAsync(ServerDb db, User changed)
    {
        if (changed.Level == AccessLevel.Administrator && changed.IsActive)
            return false;

        return !await db.Users.AnyAsync(u => u.Id != changed.Id && u.Level == AccessLevel.Administrator && u.IsActive);
    }

    // ==== Gruppen ====

    private static async Task<IResult> ListGroupsAsync(ServerDb db) =>
        Results.Ok((await db.Groups.Include(g => g.Users).OrderBy(g => g.Name).ToListAsync()).Select(g => new
        {
            id = g.Id,
            name = g.Name,
            description = g.Description,
            permissions = Permissions.ToNames(g.Permissions),
            members = g.Users.OrderBy(u => u.UserName).Select(u => u.UserName).ToArray()
        }));

    private static async Task<IResult> CreateGroupAsync(GroupInput input, HttpContext context, ServerDb db, AuditLog audit)
    {
        var actor = CurrentUser.Of(context)!;
        var group = new Group();

        if (await ApplyAsync(group, input, actor, db) is { } problem)
            return problem;

        db.Groups.Add(group);
        audit.Add(actor.User.UserName, $"Gruppe „{group.Name}“ angelegt mit den Rechten: {Describe(group.Permissions)}.");
        await db.SaveChangesAsync();

        return Results.Ok(new { id = group.Id });
    }

    private static async Task<IResult> UpdateGroupAsync(int id, GroupInput input, HttpContext context, ServerDb db, AuditLog audit)
    {
        var actor = CurrentUser.Of(context)!;

        if (await db.Groups.FirstOrDefaultAsync(g => g.Id == id) is not { } group)
            return NotFound();

        if (await ApplyAsync(group, input, actor, db) is { } problem)
            return problem;

        audit.Add(actor.User.UserName, $"Gruppe „{group.Name}“ geändert, Rechte: {Describe(group.Permissions)}.");
        await db.SaveChangesAsync();

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteGroupAsync(int id, HttpContext context, ServerDb db, AuditLog audit)
    {
        var actor = CurrentUser.Of(context)!;

        if (await db.Groups.FirstOrDefaultAsync(g => g.Id == id) is not { } group)
            return Results.NoContent();

        // Das Löschen nimmt den Mitgliedern Rechte - das darf nur, wer sie selbst hat.
        if (!actor.Permissions.Has(group.Permissions))
            return Forbidden("Eine Gruppe mit Rechten, die man selbst nicht hat, lässt sich nicht löschen.");

        db.Groups.Remove(group);
        audit.Add(actor.User.UserName, $"Gruppe „{group.Name}“ gelöscht.");
        await db.SaveChangesAsync();

        // Nimmt die Gruppe einem Administrator nichts: Seine Rechte kommen von der Stufe.
        return Results.NoContent();
    }

    private static async Task<IResult?> ApplyAsync(Group group, GroupInput input, CurrentUser actor, ServerDb db)
    {
        var name = (input.Name ?? "").Trim();
        var description = (input.Description ?? "").Trim();

        if (name.Length is < 2 or > 64)
            return Bad("Der Gruppenname braucht 2 bis 64 Zeichen.");

        if (description.Length > 300)
            return Bad("Die Beschreibung darf höchstens 300 Zeichen lang sein.");

        if (await db.Groups.AnyAsync(g => g.Id != group.Id && g.Name == name))
            return Bad($"Die Gruppe „{name}“ gibt es schon.");

        var permissions = Permissions.FromNames(input.Permissions);

        // Ändern heisst auch wegnehmen: Beides nur mit Rechten, die man selbst hat.
        if (!actor.Permissions.Has(permissions ^ group.Permissions))
            return Forbidden("Es lassen sich nur Rechte vergeben oder entziehen, die man selbst hat.");

        group.Name = name;
        group.Description = description;
        group.Permissions = permissions;

        return null;
    }

    private static string Describe(Permission permissions) =>
        string.Join(", ", Permissions.Catalog.Where(p => permissions.Has(p.Value)).Select(p => p.Label).DefaultIfEmpty("keine"));

    private static IResult Bad(string message) => AuthEndpoints.Error(StatusCodes.Status400BadRequest, message);

    private static IResult Forbidden(string message) => AuthEndpoints.Error(StatusCodes.Status403Forbidden, message);

    private static IResult NotFound() => AuthEndpoints.Error(StatusCodes.Status404NotFound, "Diesen Eintrag gibt es nicht mehr.");

    [GeneratedRegex(@"^[a-z0-9._-]{3,32}$")]
    private static partial Regex UserNamePattern();
}
