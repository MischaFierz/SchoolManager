using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace SchoolManager.App.Update;

/// <summary>Eine auf GitHub verfügbare Version.</summary>
/// <param name="Size">Grösse des Installationspakets in Bytes, wie GitHub sie angibt.</param>
/// <param name="IsDev">Ein Dev-Patch aus dem privaten Repository; sein Download braucht den eingebauten Zugang.</param>
public sealed record UpdateInfo(string Version, string DownloadUrl, string ReleaseUrl, long Size, bool IsDev = false)
{
    /// <summary>Die Nummer für die Anzeige, bei einem Dev-Patch mit angehängtem "-dev".</summary>
    public string Label => IsDev ? Version + "-dev" : Version;
}

/// <summary>
/// Prüft auf GitHub nach einer neueren Version und lädt bei Bedarf das
/// Installationspaket herunter. Öffentliche Releases liegen im Repository
/// MischaFierz/SchoolManager, Dev-Patches im privaten MischaFierz/SchoolManager-dev.
/// </summary>
public static class UpdateService
{
    private const string RepoOwner = "MischaFierz";
    private const string RepoName = "SchoolManager";
    private const string DevRepoName = "SchoolManager-dev";
    private const string InstallerAssetName = "SchoolManagerSetup.msi";

    /// <summary>So lange darf ein Download ohne ein einziges Byte bleiben, bevor er als stehen geblieben gilt.</summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Version dieser laufenden Installation, aus der Baugruppe gelesen.</summary>
    public static Version CurrentVersion =>
        Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

    /// <summary>
    /// Läuft gerade ein Dev-Patch? Der Release-Bau schreibt den Tag ohne "v",
    /// etwa 1.1.4-dev, in die Produktversion; selbst gebaute Fassungen haben dort kein "-dev".
    /// </summary>
    public static bool IsDevBuild =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Contains(DevTagMarker, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Die Nummer für die Anzeige, bei einem Dev-Patch mit angehängtem "-dev".</summary>
    public static string DisplayVersion => CurrentVersion.ToString(3) + (IsDevBuild ? DevTagMarker : "");

    /// <summary>Kennzeichen im Tag, an dem ein Dev-Patch zu erkennen ist.</summary>
    private const string DevTagMarker = "-dev";

    /// <summary>
    /// Nur-Lese-Zugang zum privaten Repository der Dev-Patches. Der Release-Bau
    /// setzt ihn aus einem Secret ein; selbst gebaute Fassungen haben keinen.
    /// </summary>
    private static string DevReleasesToken =>
        Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "DevReleasesToken")?.Value ?? "";

    /// <summary>Kann diese Fassung Dev-Patches sehen und herunterladen?</summary>
    public static bool HasDevAccess => DevReleasesToken.Length > 0;

    /// <summary>
    /// Fragt die neueste Veröffentlichung ab; liefert null, wenn keine neuere
    /// Version vorliegt. Im Entwicklermodus mit eingeschaltetem Patch-Kanal
    /// werden stattdessen die Dev-Patches durchsucht.
    /// </summary>
    public static async Task<UpdateInfo?> CheckForUpdateAsync() =>
        DevMode.UseDevPatches ? await CheckForDevPatchAsync() : await CheckForReleaseAsync();

    private static async Task<UpdateInfo?> CheckForReleaseAsync()
    {
        // "releases/latest" überspringt Vorabversionen von sich aus.
        var release = await GetFromGitHubAsync<GitHubRelease>(RepoName, "releases/latest");

        return release is null ? null : ToUpdate(release);
    }

    /// <summary>Der neueste Dev-Patch, der neuer ist als die laufende Version.</summary>
    private static async Task<UpdateInfo?> CheckForDevPatchAsync() =>
        (await DevReleasesAsync()).FirstOrDefault(info => Version.Parse(info.Version) > CurrentVersion);

    /// <summary>
    /// Die neueste öffentliche Veröffentlichung - auch dann, wenn sie älter ist
    /// als die laufende Version. Das ist der Weg zurück aus dem
    /// Entwicklermodus: Von einem Dev-Patch aus geht es abwärts.
    /// </summary>
    public static async Task<UpdateInfo?> LatestReleaseAsync()
    {
        var release = await GetFromGitHubAsync<GitHubRelease>(RepoName, "releases/latest");

        return release is null ? null : ToInfo(release);
    }

    /// <summary>
    /// Alle öffentlichen Versionen mit Installationspaket, die neueste zuerst -
    /// für den schnellen Wechsel im Entwicklermodus, auch abwärts.
    /// </summary>
    public static async Task<IReadOnlyList<UpdateInfo>> PublicReleasesAsync()
    {
        var releases = await GetFromGitHubAsync<List<GitHubRelease>>(RepoName, "releases?per_page=50") ?? [];

        return releases
            .Where(release => !release.Draft && !release.Prerelease)
            .Select(release => ToInfo(release))
            .OfType<UpdateInfo>()
            .OrderByDescending(info => Version.Parse(info.Version))
            .ToList();
    }

    /// <summary>
    /// Alle Dev-Patches aus dem privaten Repository, der neueste zuerst. Ohne
    /// eingebauten Zugang gibt es keine - das ist kein Fehler, sondern der Fall
    /// jeder selbst gebauten Fassung.
    /// </summary>
    public static async Task<IReadOnlyList<UpdateInfo>> DevReleasesAsync()
    {
        if (!HasDevAccess)
            return [];

        var releases = await GetFromGitHubAsync<List<GitHubRelease>>(
            DevRepoName, "releases?per_page=50", DevReleasesToken) ?? [];

        return releases
            .Where(release => !release.Draft)
            .Where(release => release.TagName.Contains(DevTagMarker, StringComparison.OrdinalIgnoreCase))
            .Select(release => ToInfo(release, isDev: true))
            .OfType<UpdateInfo>()
            .OrderByDescending(info => Version.Parse(info.Version))
            .ToList();
    }

    /// <summary>
    /// Fragt die GitHub-API ab. Null heisst: Dort gibt es nichts (404). Jeder
    /// andere Fehlschlag wird gemeldet - als „kein Update“ verschluckt, stünde in
    /// den Einstellungen „Sie verwenden bereits die aktuellste Version“, obwohl
    /// gar nicht nachgesehen werden konnte.
    /// </summary>
    private static async Task<T?> GetFromGitHubAsync<T>(string repository, string path, string? token = null) where T : class
    {
        using var http = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.github.com/repos/{RepoOwner}/{repository}/{path}");

        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await http.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (token is not null && response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new HttpRequestException(
                "Der eingebaute Zugang zu den Dev-Versionen gilt nicht mehr - eine neuere Fassung bringt einen gültigen mit.");

        // Ohne Anmeldung beantwortet GitHub nur 60 Anfragen je Stunde und
        // Internetadresse - in einem Schulnetz teilen sich viele Rechner eine.
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new HttpRequestException(
                "GitHub nimmt aus diesem Netz gerade keine weiteren Anfragen an. Bitte in einer Stunde nochmals versuchen.");

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"GitHub antwortete mit {(int)response.StatusCode} {response.ReasonPhrase}.");

        return JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync());
    }

    /// <summary>Macht aus einer Veröffentlichung ein Update - oder null, wenn sie nicht neuer ist.</summary>
    private static UpdateInfo? ToUpdate(GitHubRelease release) =>
        ToInfo(release) is { } info && Version.Parse(info.Version) > CurrentVersion ? info : null;

    /// <summary>Liest Version und Installationspaket aus einer Veröffentlichung.</summary>
    private static UpdateInfo? ToInfo(GitHubRelease release, bool isDev = false)
    {
        if (!TryParseVersion(release.TagName, out var version))
            return null;

        var asset = release.Assets.FirstOrDefault(a =>
            a.Name.Equals(InstallerAssetName, StringComparison.OrdinalIgnoreCase));

        if (asset is null)
            return null;

        // Aus einem privaten Repository lädt nur die API-Adresse mit Zugang;
        // die Browser-Adresse verlangte eine Anmeldung bei GitHub.
        var downloadUrl = isDev ? asset.Url : asset.BrowserDownloadUrl;

        return new UpdateInfo(Normalize(version).ToString(3), downloadUrl, release.HtmlUrl, asset.Size, isDev);
    }

    /// <summary>
    /// Lädt das Installationspaket in den Temp-Ordner herunter und gibt den Pfad
    /// zurück. Meldet den Fortschritt in Prozent und prüft zum Schluss die Grösse:
    /// Ein abgeschnittenes Paket liesse msiexec scheitern, ohne dass jemand erführe, warum.
    /// </summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<int>? progress = null)
    {
        using var http = CreateClient();
        using var stalled = new CancellationTokenSource();
        using var request = new HttpRequestMessage(HttpMethod.Get, info.DownloadUrl);

        if (info.IsDev)
        {
            // GitHub leitet auf eine signierte Adresse um; den Zugang schickt
            // HttpClient dorthin von sich aus nicht mit - so soll es auch sein.
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", DevReleasesToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        }

        var path = Path.Combine(Path.GetTempPath(), $"SchoolManagerSetup-{info.Label}.msi");
        var buffer = new byte[81920];
        long received = 0;
        var lastPercent = -1;

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        if (!response.IsSuccessStatusCode)
            throw new IOException(
                $"GitHub gab das Installationspaket nicht heraus ({(int)response.StatusCode} {response.ReasonPhrase}).");

        await using (var stream = await response.Content.ReadAsStreamAsync())
        await using (var file = File.Create(path))
        {
            while (true)
            {
                stalled.CancelAfter(StallTimeout);

                int read;

                try
                {
                    read = await stream.ReadAsync(buffer, stalled.Token);
                }
                catch (OperationCanceledException) when (stalled.IsCancellationRequested)
                {
                    throw new IOException(
                        $"Der Download ist stehen geblieben - seit {StallTimeout.TotalSeconds:0} Sekunden kam nichts mehr an. Bitte nochmals versuchen.");
                }

                if (read == 0)
                    break;

                await file.WriteAsync(buffer.AsMemory(0, read));
                received += read;

                if (info.Size <= 0)
                    continue;

                var percent = (int)(received * 100 / info.Size);

                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    progress?.Report(percent);
                }
            }
        }

        if (info.Size > 0 && received != info.Size)
            throw new IOException(
                $"Das Installationspaket kam unvollständig an ({received:N0} von {info.Size:N0} Bytes). Bitte nochmals versuchen.");

        return path;
    }

    /// <summary>
    /// Startet das Skript, das das Update einspielt, und beendet School Manager.
    ///
    /// Das Skript wartet, bis die App wirklich beendet ist - vorher belegt sie
    /// SchoolManager.exe, und das Setup könnte die Datei nicht ersetzen. Danach
    /// installiert es, wartet auf das Ende und startet School Manager wieder:
    /// nach Erfolg die neue Version, nach einem Fehlschlag mit einer Meldung die
    /// bisherige, die das Setup dann unverändert zurücklässt.
    /// </summary>
    public static void RunInstallerAndExit(string installerPath)
    {
        var script = WriteInstallScript();
        var program = DevBackupService.RestartPath(UninstallService.Installed);

        // Die Pfade gehen als Argumente mit, statt im Skript zu stehen: cmd liest
        // Skripte in der OEM-Codepage, ein Umlaut im Benutzernamen ergäbe dort
        // einen falschen Pfad. Das äussere Paar Anführungszeichen verlangt cmd /c.
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"\"{script}\" \"{installerPath}\" \"{program}\"\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        });

        Application.Current.Shutdown();
    }

    /// <summary>
    /// Schreibt das Update-Skript - reines ASCII; das Installationspaket kommt
    /// als %1 herein, die danach zu startende Programmdatei als %2.
    /// </summary>
    private static string WriteInstallScript()
    {
        var script = Path.Combine(Path.GetTempPath(), "schoolmanager-update.cmd");
        var pid = Environment.ProcessId;
        var image = Path.GetFileName(Environment.ProcessPath) ?? "SchoolManager.exe";

        var text = $"""
            @echo off
            setlocal
            set "protokoll=%TEMP%\schoolmanager-update.log"
            set "setupprotokoll=%TEMP%\schoolmanager-update-msi.log"
            echo ==== %date% %time% %~nx1 ==== >>"%protokoll%"

            rem Warten, bis School Manager beendet ist; haengt das Beenden, nach 30 s nachhelfen.
            set /a sekunden=0
            :warten
            tasklist /fi "PID eq {pid}" /fi "IMAGENAME eq {image}" /nh 2>nul | find "{pid}" >nul
            if errorlevel 1 goto installieren
            set /a sekunden+=1
            if %sekunden% geq 30 taskkill /pid {pid} /fi "IMAGENAME eq {image}" /f >nul 2>nul
            ping -n 2 127.0.0.1 >nul
            goto warten

            :installieren
            echo School Manager beendet nach %sekunden% s >>"%protokoll%"
            start /wait "" msiexec /i "%~1" /qb /l*v "%setupprotokoll%"
            set "ergebnis=%errorlevel%"
            echo Installation beendet mit %ergebnis% >>"%protokoll%"

            rem 0 ist erledigt, 3010 heisst "erledigt, Neustart waere gut".
            if "%ergebnis%"=="0" goto starten
            if "%ergebnis%"=="3010" goto starten
            powershell -NoProfile -Command "Add-Type -AssemblyName PresentationFramework; [void][System.Windows.MessageBox]::Show('Das Update ist gescheitert (Fehler %ergebnis%). School Manager startet wieder in der bisherigen Version. Das Installationspaket liegt unter %~1 und laesst sich von Hand starten; Einzelheiten stehen in %setupprotokoll%.', 'School Manager')"

            :starten
            start "" "%~2"
            (goto) 2>nul & del "%~f0"
            """;

        // Mit blossen LF-Zeilenenden findet cmd Sprungmarken nicht zuverlässig.
        File.WriteAllText(script, text.ReplaceLineEndings("\r\n"));

        return script;
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

        /// <summary>Vorabversionen - Dev-Patches und Archiv; öffentlich ist, was keine ist.</summary>
        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = "";

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        /// <summary>Die API-Adresse des Pakets; aus einem privaten Repository der einzige Weg mit Zugang.</summary>
        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }
}
