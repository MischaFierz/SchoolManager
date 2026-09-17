using Microsoft.EntityFrameworkCore;
using SchoolManager.Server.Data;
using SchoolManager.Server.GitHub;
using SchoolManager.Server.Security;

namespace SchoolManager.Server.Services;

/// <summary>Eine Version, wie sie bei GitHub liegt.</summary>
/// <param name="InPublicRepo">Liegt im öffentlichen Repository.</param>
/// <param name="IsLegacyDev">Eine alte Vorabversion im öffentlichen Repository, aus der Zeit vor dem privaten.</param>
public sealed record CatalogRelease(
    GitHubRelease Source,
    string Repo,
    Version Version,
    bool IsDev,
    bool InPublicRepo,
    bool IsLegacyDev)
{
    public string Tag => Source.TagName;

    public GitHubAsset? Installer => Source.Assets.FirstOrDefault(asset =>
        asset.Name.Equals(GitHubService.InstallerAssetName, StringComparison.OrdinalIgnoreCase));

    public bool HasExe => Source.Assets.Any(asset =>
        asset.Name.Equals(GitHubService.ExeAssetName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Beide Dateien sind da - der Bau ist durch und die Version zu haben.</summary>
    public bool IsComplete => Installer is not null && HasExe;

    public string VersionText => Version.ToString(3);
}

/// <summary>
/// Führt die Releases beider Repositories zusammen und entscheidet, welche
/// Version ein Benutzer beziehen darf.
/// </summary>
public sealed class ReleaseCatalog(GitHubService github, ServerDb db)
{
    public async Task<IReadOnlyList<CatalogRelease>> AllAsync()
    {
        var publicReleases = await github.ReleasesAsync(github.PublicRepo);
        var devReleases = await github.ReleasesAsync(github.DevRepo);

        var result = new List<CatalogRelease>();

        foreach (var release in publicReleases.Where(r => !r.Draft))
        {
            if (VersionTag.TryParse(release.TagName, out var version, out var isDev))
                result.Add(new CatalogRelease(release, github.PublicRepo, version, isDev, true, IsLegacyDev: isDev || release.Prerelease));
        }

        foreach (var release in devReleases.Where(r => !r.Draft))
        {
            if (VersionTag.TryParse(release.TagName, out var version, out var isDev) && isDev)
                result.Add(new CatalogRelease(release, github.DevRepo, version, true, false, false));
        }

        return result
            .OrderByDescending(r => r.Version)
            .ThenBy(r => r.IsDev)
            .ToList();
    }

    /// <summary>
    /// Was dieser Benutzer beziehen darf, die neueste Version zuerst. Ohne
    /// Anmeldung, ohne Entwicklermodus oder mit „nur öffentliche Versionen“ sind
    /// es die öffentlichen Releases.
    /// </summary>
    public async Task<IReadOnlyList<CatalogRelease>> AllowedAsync(CurrentUser? user, bool includeDev)
    {
        var all = await AllAsync();
        var allowed = all.Where(r => r.InPublicRepo && !r.IsLegacyDev && r.Installer is not null).ToList();

        if (!includeDev || user is null || user.User.PublicOnly || !user.Permissions.Has(Permission.DevMode))
            return allowed;

        var dev = all.Where(r => !r.InPublicRepo && r.Installer is not null).ToList();

        if (!user.Permissions.Has(Permission.AllDevUpdates))
        {
            var groupIds = user.User.Groups.Select(g => g.Id).ToList();
            var tags = dev.Select(r => r.Tag).ToList();

            var approved = await db.DevApprovals
                .Where(a => tags.Contains(a.Tag))
                .Where(a => a.ForAllDevelopers
                            || a.Users.Any(u => u.Id == user.User.Id)
                            || a.Groups.Any(g => groupIds.Contains(g.Id)))
                .Select(a => a.Tag)
                .ToListAsync();

            dev = dev.Where(r => approved.Contains(r.Tag)).ToList();
        }

        return allowed.Concat(dev)
            .OrderByDescending(r => r.Version)
            .ThenBy(r => r.IsDev)
            .ToList();
    }

    public async Task<Dictionary<string, string>> NotesAsync() =>
        await db.ReleaseNotes.ToDictionaryAsync(n => n.Tag, n => n.Text);
}

public static class VersionTag
{
    /// <summary>Liest v1.2.0 oder v1.2.0-dev. Nur Tags dieser Form gelten als Version.</summary>
    public static bool TryParse(string? tag, out Version version, out bool isDev)
    {
        version = new Version(0, 0, 0);
        isDev = false;

        if (string.IsNullOrWhiteSpace(tag) || !tag.StartsWith('v'))
            return false;

        var number = tag[1..];
        var marker = number.IndexOf('-');

        if (marker >= 0)
        {
            isDev = number[marker..].Equals("-dev", StringComparison.OrdinalIgnoreCase);
            number = number[..marker];
        }

        if (!Version.TryParse(number, out var parsed) || parsed.Build < 0 || parsed.Revision >= 0)
            return false;

        version = parsed;
        return true;
    }

    /// <summary>Nimmt nur X.Y.Z mit Zahlen an.</summary>
    public static bool TryParseNumber(string? text, out Version version)
    {
        version = new Version(0, 0, 0);

        return !string.IsNullOrWhiteSpace(text)
               && System.Text.RegularExpressions.Regex.IsMatch(text, @"^\d{1,4}\.\d{1,4}\.\d{1,4}$")
               && Version.TryParse(text, out version!);
    }
}
