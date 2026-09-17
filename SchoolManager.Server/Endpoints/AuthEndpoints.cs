using Microsoft.EntityFrameworkCore;
using SchoolManager.Server.Data;
using SchoolManager.Server.Security;
using SchoolManager.Server.Services;

namespace SchoolManager.Server.Endpoints;

public static class AuthEndpoints
{
    /// <summary>So viele Fehlversuche hintereinander sperren ein Konto vorübergehend.</summary>
    private const int MaxFailedLogins = 5;

    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    public sealed record LoginRequest(string? UserName, string? Password, string? Client);

    public sealed record PasswordChange(string? CurrentPassword, string? NewPassword);

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", LoginAsync).RequireRateLimiting("login");

        app.MapPost("/api/auth/logout", async (HttpContext context, SessionService sessions) =>
        {
            var header = context.Request.Headers.Authorization.ToString();

            if (header.StartsWith("Bearer ", StringComparison.Ordinal))
                await sessions.RevokeAsync(header["Bearer ".Length..].Trim());

            return Results.NoContent();
        });

        app.MapGet("/api/me", (HttpContext context) => Results.Ok(Me(CurrentUser.Of(context)!)))
            .RequireSignIn();

        app.MapPost("/api/me/password", ChangePasswordAsync).RequireSignIn().RequireRateLimiting("password");
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request, ServerDb db, SessionService sessions, AuditLog audit, TimeProvider clock)
    {
        var userName = Normalize(request.UserName);
        var password = request.Password ?? "";
        var forApp = string.Equals(request.Client, "app", StringComparison.OrdinalIgnoreCase);
        var now = clock.GetUtcNow();

        var user = userName.Length == 0
            ? null
            : await db.Users.Include(u => u.Groups).FirstOrDefaultAsync(u => u.UserName == userName);

        if (user is null)
        {
            PasswordHasher.VerifyDecoy(password);
            return Error(StatusCodes.Status401Unauthorized, "Benutzername oder Passwort stimmt nicht.");
        }

        if (user.LockedUntil > now)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((user.LockedUntil.Value - now).TotalMinutes));
            return Error(StatusCodes.Status429TooManyRequests,
                $"Zu viele Fehlversuche. Bitte in {minutes} Minuten nochmals versuchen.");
        }

        if (!PasswordHasher.Verify(password, user.PasswordHash))
        {
            user.FailedLogins++;

            if (user.FailedLogins >= MaxFailedLogins)
            {
                user.FailedLogins = 0;
                user.LockedUntil = now + LockDuration;
                audit.Add("System", $"Konto „{user.UserName}“ nach {MaxFailedLogins} Fehlversuchen für {LockDuration.TotalMinutes:0} Minuten gesperrt.");
            }

            await db.SaveChangesAsync();
            return Error(StatusCodes.Status401Unauthorized, "Benutzername oder Passwort stimmt nicht.");
        }

        // Erst nach dem richtigen Passwort verraten, dass das Konto gesperrt ist.
        if (!user.IsActive)
            return Error(StatusCodes.Status403Forbidden, "Dieses Konto ist gesperrt.");

        var permissions = CurrentUser.EffectivePermissions(user);

        if (forApp && !permissions.Has(Permission.DevMode))
            return Error(StatusCodes.Status403Forbidden, "Dieses Konto darf den Entwicklermodus nicht öffnen.");

        if (forApp && user.MustChangePassword)
            return Error(StatusCodes.Status403Forbidden,
                "Für dieses Konto muss zuerst ein neues Passwort gesetzt werden - bitte einmal im Admin-Panel anmelden.");

        user.FailedLogins = 0;
        user.LockedUntil = null;
        user.LastLoginAt = now;
        await db.SaveChangesAsync();

        var token = await sessions.CreateAsync(user, forApp ? SessionKind.App : SessionKind.Panel);
        var session = await sessions.FindAsync(token);

        return Results.Ok(new { token, user = Me(new CurrentUser(session!)) });
    }

    private static async Task<IResult> ChangePasswordAsync(
        PasswordChange request, HttpContext context, ServerDb db, SessionService sessions, AuditLog audit)
    {
        var current = CurrentUser.Of(context)!;
        var user = await db.Users.FirstAsync(u => u.Id == current.User.Id);

        if (!PasswordHasher.Verify(request.CurrentPassword ?? "", user.PasswordHash))
            return Error(StatusCodes.Status400BadRequest, "Das bisherige Passwort stimmt nicht.");

        if (PasswordHasher.Weakness(request.NewPassword) is { } weakness)
            return Error(StatusCodes.Status400BadRequest, weakness);

        if (request.NewPassword == request.CurrentPassword)
            return Error(StatusCodes.Status400BadRequest, "Das neue Passwort muss sich vom bisherigen unterscheiden.");

        user.PasswordHash = PasswordHasher.Hash(request.NewPassword!);
        user.MustChangePassword = false;
        audit.Add(user.UserName, "Eigenes Passwort geändert.");
        await db.SaveChangesAsync();

        // Andere Geräte abmelden - wer das Passwort ändert, will oft genau das.
        await sessions.RevokeAllAsync(user.Id, exceptSessionId: current.Session.Id);

        return Results.NoContent();
    }

    public static object Me(CurrentUser user) => new
    {
        id = user.User.Id,
        userName = user.User.UserName,
        displayName = user.User.DisplayName,
        level = user.User.Level,
        permissions = Permissions.ToNames(user.Permissions),
        groups = user.User.Groups.Select(g => g.Name).OrderBy(n => n).ToArray(),
        publicOnly = user.User.PublicOnly,
        mustChangePassword = user.User.MustChangePassword
    };

    public static string Normalize(string? userName) => (userName ?? "").Trim().ToLowerInvariant();

    public static IResult Error(int status, string message) => Results.Json(new { error = message }, statusCode: status);
}
