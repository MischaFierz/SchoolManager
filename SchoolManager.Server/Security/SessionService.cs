using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SchoolManager.Server.Data;

namespace SchoolManager.Server.Security;

/// <summary>
/// Anmeldungen als zufällige Tokens. Gespeichert wird nur ihr Abdruck: Wer die
/// Datenbank in die Hände bekommt, kann sich damit nicht anmelden.
/// </summary>
public sealed class SessionService(ServerDb db, TimeProvider clock)
{
    /// <summary>Im Panel gilt eine Anmeldung einen Arbeitstag.</summary>
    public static readonly TimeSpan PanelLifetime = TimeSpan.FromHours(12);

    /// <summary>In der App bleibt man angemeldet, solange man sie ab und zu startet.</summary>
    public static readonly TimeSpan AppLifetime = TimeSpan.FromDays(60);

    public async Task<string> CreateAsync(User user, SessionKind kind)
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        var now = clock.GetUtcNow();

        db.Sessions.Add(new Session
        {
            UserId = user.Id,
            TokenHash = HashToken(token),
            Kind = kind,
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now + Lifetime(kind)
        });

        // Abgelaufene Anmeldungen dieses Benutzers bei der Gelegenheit wegräumen.
        await db.Sessions.Where(s => s.UserId == user.Id && s.ExpiresAt < now).ExecuteDeleteAsync();
        await db.SaveChangesAsync();

        return token;
    }

    /// <summary>Findet die gültige Anmeldung zu einem Token samt Benutzer und Gruppen.</summary>
    public async Task<Session?> FindAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100)
            return null;

        var hash = HashToken(token);
        var now = clock.GetUtcNow();

        var session = await db.Sessions
            .Include(s => s.User!).ThenInclude(u => u.Groups)
            .FirstOrDefaultAsync(s => s.TokenHash == hash);

        if (session?.User is null || session.ExpiresAt < now || !session.User.IsActive)
            return null;

        // In der App verlängert jede Nutzung die Anmeldung; höchstens einmal
        // pro Stunde gespeichert, damit nicht jede Anfrage schreibt.
        if (now - session.LastSeenAt > TimeSpan.FromHours(1))
        {
            session.LastSeenAt = now;

            if (session.Kind == SessionKind.App)
                session.ExpiresAt = now + AppLifetime;

            await db.SaveChangesAsync();
        }

        return session;
    }

    public Task RevokeAsync(string token)
    {
        var hash = HashToken(token);
        return db.Sessions.Where(s => s.TokenHash == hash).ExecuteDeleteAsync();
    }

    /// <summary>Meldet einen Benutzer überall ab, auf Wunsch ausser in der laufenden Anmeldung.</summary>
    public Task RevokeAllAsync(int userId, int? exceptSessionId = null) =>
        db.Sessions.Where(s => s.UserId == userId && s.Id != exceptSessionId).ExecuteDeleteAsync();

    private static TimeSpan Lifetime(SessionKind kind) => kind == SessionKind.App ? AppLifetime : PanelLifetime;

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
