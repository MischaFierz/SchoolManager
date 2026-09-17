using SchoolManager.Server.Data;

namespace SchoolManager.Server.Security;

/// <summary>Der angemeldete Benutzer einer Anfrage, samt seinen wirksamen Rechten.</summary>
public sealed class CurrentUser(Session session)
{
    public Session Session { get; } = session;

    public User User => Session.User!;

    public Permission Permissions { get; } = EffectivePermissions(session.User!);

    public bool IsAdministrator => User.Level == AccessLevel.Administrator;

    public static Permission EffectivePermissions(User user) =>
        user.Groups.Aggregate(
            Security.Permissions.ForLevel(user.Level) | user.ExtraPermissions,
            (granted, group) => granted | group.Permissions);

    private const string ItemKey = "SchoolManager.CurrentUser";

    public static CurrentUser? Of(HttpContext context) => context.Items[ItemKey] as CurrentUser;

    /// <summary>Liest das Token aus der Kopfzeile Authorization und merkt sich die Anmeldung.</summary>
    public static async Task ResolveAsync(HttpContext context, Func<Task> next)
    {
        var header = context.Request.Headers.Authorization.ToString();

        if (header.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            var sessions = context.RequestServices.GetRequiredService<SessionService>();

            if (await sessions.FindAsync(header["Bearer ".Length..].Trim()) is { } session)
                context.Items[ItemKey] = new CurrentUser(session);
        }

        await next();
    }
}

public static class AuthorizationExtensions
{
    /// <summary>
    /// Lässt nur angemeldete Benutzer durch, die alle genannten Rechte haben.
    /// Wer sein Passwort ändern muss, kommt vorher nirgends hin.
    /// </summary>
    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, Permission permission) =>
        builder.Require(granted => granted.Has(permission));

    /// <summary>Wie <see cref="RequirePermission"/>, aber eines der genannten Rechte genügt.</summary>
    public static RouteHandlerBuilder RequireAnyPermission(this RouteHandlerBuilder builder, Permission permissions) =>
        builder.Require(granted => granted.HasAny(permissions));

    private static RouteHandlerBuilder Require(this RouteHandlerBuilder builder, Func<Permission, bool> allowed) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var user = CurrentUser.Of(context.HttpContext);

            if (user is null)
                return Results.Json(new { error = "Bitte anmelden." }, statusCode: StatusCodes.Status401Unauthorized);

            if (user.User.MustChangePassword)
                return Results.Json(new { error = "Bitte zuerst ein neues Passwort setzen." }, statusCode: StatusCodes.Status403Forbidden);

            if (!allowed(user.Permissions))
                return Results.Json(new { error = "Dafür fehlt die Berechtigung." }, statusCode: StatusCodes.Status403Forbidden);

            return await next(context);
        });

    /// <summary>Nur angemeldet, ohne besonderes Recht - etwa für das eigene Passwort.</summary>
    public static RouteHandlerBuilder RequireSignIn(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
            CurrentUser.Of(context.HttpContext) is null
                ? Results.Json(new { error = "Bitte anmelden." }, statusCode: StatusCodes.Status401Unauthorized)
                : await next(context));
}
