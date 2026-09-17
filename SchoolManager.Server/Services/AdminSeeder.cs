using Microsoft.EntityFrameworkCore;
using SchoolManager.Server.Data;
using SchoolManager.Server.Endpoints;
using SchoolManager.Server.Security;

namespace SchoolManager.Server.Services;

/// <summary>Sorgt dafür, dass es nach dem ersten Start einen Administrator gibt.</summary>
public static class AdminSeeder
{
    /// <summary>
    /// Legt den ersten Administrator an, solange die Datenbank leer ist.
    /// Name und Passwort kommen aus Admin:UserName und Admin:Password. Fehlt
    /// das Passwort, wird eines erzeugt, einmal ins Log geschrieben und muss
    /// bei der ersten Anmeldung ersetzt werden.
    /// </summary>
    public static async Task EnsureAdministratorAsync(ServerDb db, IConfiguration configuration, ILogger logger)
    {
        if (await db.Users.AnyAsync())
            return;

        var userName = AuthEndpoints.Normalize(configuration["Admin:UserName"] ?? "admin");
        var configured = configuration["Admin:Password"];
        var password = string.IsNullOrEmpty(configured) ? PasswordHasher.Generate() : configured;

        if (PasswordHasher.Weakness(password) is { } weakness)
            throw new InvalidOperationException($"Admin:Password taugt nicht: {weakness}");

        db.Users.Add(new User
        {
            UserName = userName,
            DisplayName = "Administrator",
            Level = AccessLevel.Administrator,
            PasswordHash = PasswordHasher.Hash(password),
            MustChangePassword = string.IsNullOrEmpty(configured),
            CreatedAt = DateTimeOffset.UtcNow
        });

        db.AuditEntries.Add(new AuditEntry
        {
            Time = DateTimeOffset.UtcNow,
            Actor = "System",
            Action = $"Erster Administrator „{userName}“ angelegt."
        });

        await db.SaveChangesAsync();

        if (string.IsNullOrEmpty(configured))
            logger.LogWarning(
                "Erster Administrator angelegt: Benutzer {UserName}, einmaliges Passwort {Password} - bei der ersten Anmeldung muss es ersetzt werden.",
                userName, password);
        else
            logger.LogInformation("Erster Administrator {UserName} mit dem Passwort aus Admin:Password angelegt.", userName);
    }

    /// <summary>Setzt ein neues, zufälliges Passwort und gibt es auf der Konsole aus.</summary>
    public static async Task ResetPasswordAsync(ServerDb db, string userName)
    {
        var name = AuthEndpoints.Normalize(userName);
        var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == name);

        if (user is null)
        {
            Console.Error.WriteLine($"Den Benutzer „{name}“ gibt es nicht.");
            Environment.ExitCode = 1;
            return;
        }

        var password = PasswordHasher.Generate();

        user.PasswordHash = PasswordHasher.Hash(password);
        user.MustChangePassword = true;
        user.IsActive = true;
        user.FailedLogins = 0;
        user.LockedUntil = null;

        await db.Sessions.Where(s => s.UserId == user.Id).ExecuteDeleteAsync();

        db.AuditEntries.Add(new AuditEntry
        {
            Time = DateTimeOffset.UtcNow,
            Actor = "Konsole",
            Action = $"Passwort von „{name}“ über die Befehlszeile zurückgesetzt."
        });

        await db.SaveChangesAsync();

        Console.WriteLine($"Neues einmaliges Passwort für {name}: {password}");
        Console.WriteLine("Bei der nächsten Anmeldung im Panel muss es ersetzt werden.");
    }
}
