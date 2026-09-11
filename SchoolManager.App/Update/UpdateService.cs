using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace SchoolManager.App.Update;

/// <summary>Eine auf GitHub verfügbare, neuere Version.</summary>
public sealed record UpdateInfo(string Version, string DownloadUrl, string ReleaseUrl);

/// <summary>
/// Prüft auf GitHub Releases (Repository MischaFierz/SchoolManager) nach einer neueren
/// Version und lädt bei Bedarf das Installationspaket herunter.
/// </summary>
public static class UpdateService
{
    private const string RepoOwner = "MischaFierz";
    private const string RepoName = "SchoolManager";
    private const string InstallerAssetName = "SchoolManagerSetup.msi";

    /// <summary>Version dieser laufenden Installation, aus der Baugruppe gelesen.</summary>
    public static Version CurrentVersion =>
        Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

    /// <summary>Kennzeichen im Tag, an dem ein Dev-Patch zu erkennen ist.</summary>
    private const string DevTagMarker = "-dev";

    /// <summary>
    /// Fragt die neueste Veröffentlichung ab; liefert null, wenn keine neuere
    /// Version vorliegt. Im Entwicklermodus mit eingeschaltetem Patch-Kanal
    /// werden stattdessen die Dev-Patches durchsucht.
    /// </summary>
    public static async Task<UpdateInfo?> CheckForUpdateAsync() =>
        DevMode.UseDevPatches ? await CheckForDevPatchAsync() : await CheckForReleaseAsync();

    private static async Task<UpdateInfo?> CheckForReleaseAsync()
    {
        using var http = CreateClient();

        // "releases/latest" überspringt Vorabversionen von sich aus - Dev-Patches
        // kommen normalen Nutzern damit gar nicht erst unter die Augen.
        using var response = await http.GetAsync(
            $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");

        // Noch keine Veröffentlichung vorhanden oder nicht erreichbar - dann gibt es nichts zu holen.
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync();
        var release = JsonSerializer.Deserialize<GitHubRelease>(json);

        return release is null ? null : ToUpdate(release);
    }

    /// <summary>
    /// Sucht den neuesten Dev-Patch. Das sind Vorabversionen, deren Tag
    /// „-dev“ enthält - etwa v1.0.1-dev. Ältere Vorabversionen ohne diese
    /// Kennzeichnung bleiben aussen vor; sie sind nur noch Archiv.
    /// </summary>
    private static async Task<UpdateInfo?> CheckForDevPatchAsync()
    {
        using var http = CreateClient();

        using var response = await http.GetAsync(
            $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases?per_page=50");

        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync();
        var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(json);

        if (releases is null)
            return null;

        return releases
            .Where(release => !release.Draft)
            .Where(release => release.TagName.Contains(DevTagMarker, StringComparison.OrdinalIgnoreCase))
            .Select(ToUpdate)
            .Where(update => update is not null)
            .OrderByDescending(update => Version.Parse(update!.Version))
            .FirstOrDefault();
    }

    /// <summary>
    /// Die neueste öffentliche Veröffentlichung - auch dann, wenn sie älter ist
    /// als die laufende Version. Das ist der Weg zurück aus dem
    /// Entwicklermodus: Von einem Dev-Patch aus geht es abwärts.
    /// </summary>
    public static async Task<UpdateInfo?> LatestReleaseAsync()
    {
        using var http = CreateClient();

        using var response = await http.GetAsync(
            $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");

        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync();
        var release = JsonSerializer.Deserialize<GitHubRelease>(json);

        return release is null ? null : ToInfo(release);
    }

    /// <summary>Macht aus einer Veröffentlichung ein Update - oder null, wenn sie nicht neuer ist.</summary>
    private static UpdateInfo? ToUpdate(GitHubRelease release) =>
        ToInfo(release) is { } info && Version.Parse(info.Version) > CurrentVersion ? info : null;

    /// <summary>Liest Version und Installationspaket aus einer Veröffentlichung.</summary>
    private static UpdateInfo? ToInfo(GitHubRelease release)
    {
        if (!TryParseVersion(release.TagName, out var version))
            return null;

        var asset = release.Assets.FirstOrDefault(a =>
            a.Name.Equals(InstallerAssetName, StringComparison.OrdinalIgnoreCase));

        if (asset is null)
            return null;

        return new UpdateInfo(Normalize(version).ToString(3), asset.BrowserDownloadUrl, release.HtmlUrl);
    }

    /// <summary>Lädt das Installationspaket in einen temporären Ordner herunter und gibt den Pfad zurück.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info)
    {
        using var http = CreateClient();

        var path = Path.Combine(Path.GetTempPath(), $"SchoolManagerSetup-{info.Version}.msi");

        await using (var stream = await http.GetStreamAsync(info.DownloadUrl))
        await using (var file = File.Create(path))
            await stream.CopyToAsync(file);

        return path;
    }

    /// <summary>
    /// Startet das Installationspaket, beendet School Manager, damit die Dateien ersetzt werden
    /// können, und startet die Anwendung danach automatisch neu.
    /// </summary>
    public static void RunInstallerAndExit(string installerPath)
    {
        var installedExe = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "School Manager", "SchoolManager.exe");

        // "msiexec /i" wartet, bis die (auch interaktive) Installation abgeschlossen ist;
        // erst danach - und nur bei Erfolg - wird School Manager wieder gestartet. Das läuft
        // in einem eigenen cmd-Prozess, weil sich die gerade beendete App nicht selbst
        // neu starten kann, während das Setup ihre eigene Datei ersetzt.
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c msiexec /i \"{installerPath}\" && start \"\" \"{installedExe}\"",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        Application.Current.Shutdown();
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient();

        // Die GitHub-API verlangt einen User-Agent, sonst wird jede Anfrage abgelehnt.
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SchoolManager", CurrentVersion.ToString(3)));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        return http;
    }

    private static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);

        if (string.IsNullOrWhiteSpace(tag))
            return false;

        var number = tag.TrimStart('v', 'V');

        // Ein Zusatz gehört nicht zur Zahl: v1.0.1-dev ist die Version 1.0.1.
        // Ohne dieses Abschneiden liesse sich kein einziger Dev-Patch lesen.
        if (number.IndexOf('-') is var marker && marker >= 0)
            number = number[..marker];

        return Version.TryParse(number, out version!);
    }

    /// <summary>Nur Major.Minor.Build zählen - so stört eine fehlende oder abweichende Revision nicht.</summary>
    private static Version Normalize(Version v) =>
        new(Math.Max(v.Major, 0), Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        /// <summary>Entwürfe sind noch nicht veröffentlicht und gelten nicht.</summary>
        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = "";

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";
    }
}
