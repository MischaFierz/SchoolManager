using System.Security.Cryptography;

namespace SchoolManager.Server.Security;

/// <summary>
/// Passwörter werden nie gespeichert, nur ihr PBKDF2-Abdruck mit zufälligem Salz.
/// Format: <c>pbkdf2-sha256$Durchläufe$Salz$Abdruck</c>, beides Base64. Die Zahl
/// der Durchläufe steht mit drin, damit sie später erhöht werden kann, ohne
/// bestehende Passwörter ungültig zu machen.
/// </summary>
public static class PasswordHasher
{
    private const string Scheme = "pbkdf2-sha256";

    /// <summary>Empfehlung von OWASP für PBKDF2 mit SHA-256.</summary>
    private const int Iterations = 600_000;

    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public const int MinimumLength = 10;

    /// <summary>Ein Abdruck, gegen den geprüft wird, wenn es den Benutzer gar nicht gibt.</summary>
    private static readonly Lazy<string> Decoy = new(() => Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))));

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);

        return $"{Scheme}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');

        if (parts.Length != 4 || parts[0] != Scheme || !int.TryParse(parts[1], out var iterations))
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Kostet so viel Zeit wie eine echte Prüfung. So verrät die Antwortzeit
    /// nicht, ob es einen Benutzernamen gibt.
    /// </summary>
    public static void VerifyDecoy(string password) => Verify(password, Decoy.Value);

    /// <summary>Sagt, was an einem neuen Passwort fehlt; null, wenn es taugt.</summary>
    public static string? Weakness(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinimumLength)
            return $"Das Passwort braucht mindestens {MinimumLength} Zeichen.";

        if (password.Length > 200)
            return "Das Passwort darf höchstens 200 Zeichen lang sein.";

        if (password.Distinct().Count() < 5)
            return "Das Passwort besteht aus zu wenigen verschiedenen Zeichen.";

        return null;
    }

    /// <summary>Ein zufälliges, gut tippbares Passwort ohne verwechselbare Zeichen.</summary>
    public static string Generate(int length = 24)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789-_!?#%+=";

        return new string(Enumerable.Range(0, length)
            .Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)])
            .ToArray());
    }
}
