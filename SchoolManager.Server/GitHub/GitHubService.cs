using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace SchoolManager.Server.GitHub;

public sealed class GitHubOptions
{
    public string Owner { get; set; } = "MischaFierz";

    /// <summary>Öffentliches Repository mit den Releases für alle.</summary>
    public string PublicRepo { get; set; } = "SchoolManager";

    /// <summary>Privates Repository mit den Dev-Versionen.</summary>
    public string DevRepo { get; set; } = "SchoolManager-dev";

    /// <summary>Zweig im öffentlichen Repository, von dem öffentliche Releases getaggt werden.</summary>
    public string PublicBranch { get; set; } = "master";

    /// <summary>Anfangswert für den Entwicklungszweig; im Panel änderbar.</summary>
    public string DevBranch { get; set; } = "entwicklung-1.2.0";

    /// <summary>
    /// Fein abgestuftes Token für beide Repositories: Contents lesen und
    /// schreiben, Actions lesen. Ohne Token geht nur das Lesen öffentlicher Releases.
    /// </summary>
    public string Token { get; set; } = "";
}

public sealed class GitHubException(string message) : Exception(message);

public sealed record GitHubAsset(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);

public sealed record GitHubRelease(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("prerelease")] bool Prerelease,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
    [property: JsonPropertyName("assets")] List<GitHubAsset> Assets);

public sealed record GitHubRun(
    [property: JsonPropertyName("head_branch")] string? HeadBranch,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("conclusion")] string? Conclusion,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

public sealed record GitHubCommitInfo(string Sha, string Message, DateTimeOffset? Date);

/// <summary>Alles, was der Server bei GitHub nachsieht oder auslöst.</summary>
public sealed partial class GitHubService(IHttpClientFactory httpFactory, IOptions<GitHubOptions> options, IMemoryCache cache)
{
    public const string InstallerAssetName = "SchoolManagerSetup.msi";
    public const string ExeAssetName = "SchoolManager.exe";

    private readonly GitHubOptions settings = options.Value;

    public string PublicRepo => settings.PublicRepo;
    public string DevRepo => settings.DevRepo;
    public string PublicBranch => settings.PublicBranch;
    public string Owner => settings.Owner;

    public bool HasToken => settings.Token.Length > 0;

    public string ActionsUrl(string repo) => $"https://github.com/{settings.Owner}/{repo}/actions";

    /// <summary>Die Releases eines Repositorys; eine Minute zwischengespeichert, damit nicht jeder App-Start GitHub fragt.</summary>
    public async Task<IReadOnlyList<GitHubRelease>> ReleasesAsync(string repo)
    {
        if (cache.TryGetValue(CacheKey("releases", repo), out IReadOnlyList<GitHubRelease>? cached) && cached is not null)
            return cached;

        // Ohne Token sieht der Server das private Repository gar nicht.
        if (repo == settings.DevRepo && !HasToken)
            return [];

        var releases = await GetAsync<List<GitHubRelease>>(repo, "releases?per_page=100") ?? [];

        cache.Set(CacheKey("releases", repo), (IReadOnlyList<GitHubRelease>)releases, TimeSpan.FromMinutes(1));
        return releases;
    }

    /// <summary>Die letzten Bauläufe; ein Tag erscheint darin als head_branch.</summary>
    public async Task<IReadOnlyList<GitHubRun>> RunsAsync(string repo)
    {
        if (!HasToken)
            return [];

        if (cache.TryGetValue(CacheKey("runs", repo), out IReadOnlyList<GitHubRun>? cached) && cached is not null)
            return cached;

        var page = await GetAsync<RunPage>(repo, "actions/runs?per_page=30");
        IReadOnlyList<GitHubRun> runs = page?.WorkflowRuns ?? [];

        cache.Set(CacheKey("runs", repo), runs, TimeSpan.FromSeconds(15));
        return runs;
    }

    public async Task<GitHubCommitInfo> BranchHeadAsync(string repo, string branch)
    {
        var commit = await GetAsync<CommitResponse>(repo, $"commits/{Uri.EscapeDataString(branch)}")
                     ?? throw new GitHubException($"Den Zweig „{branch}“ gibt es in {repo} nicht.");

        var message = commit.Commit.Message.Split('\n')[0];
        return new GitHubCommitInfo(commit.Sha, message, commit.Commit.Committer?.Date);
    }

    /// <summary>Liest die Versionsnummer aus der Projektdatei auf einem Zweig; null, wenn sie nicht zu finden ist.</summary>
    public async Task<string?> ProjectVersionAsync(string repo, string branch)
    {
        var file = await GetAsync<ContentResponse>(repo,
            $"contents/SchoolManager.App/SchoolManager.App.csproj?ref={Uri.EscapeDataString(branch)}");

        if (file?.Content is null)
            return null;

        var text = Encoding.UTF8.GetString(Convert.FromBase64String(file.Content.Replace("\n", "")));
        var match = VersionElement().Match(text);

        return match.Success ? match.Groups[1].Value : null;
    }

    public async Task<bool> TagExistsAsync(string repo, string tag) =>
        await GetAsync<object>(repo, $"git/ref/tags/{Uri.EscapeDataString(tag)}") is not null;

    /// <summary>Setzt einen Tag. Der Workflow des Repositorys baut daraufhin und veröffentlicht.</summary>
    public async Task CreateTagAsync(string repo, string tag, string sha)
    {
        RequireToken();

        using var response = await SendAsync(HttpMethod.Post, repo, "git/refs",
            JsonContent.Create(new { @ref = $"refs/tags/{tag}", sha }));

        await EnsureSuccessAsync(response, $"Der Tag {tag} konnte nicht gesetzt werden");
        Forget(repo);
    }

    /// <summary>Löscht ein Release und seinen Tag.</summary>
    public async Task DeleteReleaseAsync(string repo, GitHubRelease release)
    {
        RequireToken();

        using (var response = await SendAsync(HttpMethod.Delete, repo, $"releases/{release.Id}"))
        {
            if (response.StatusCode != HttpStatusCode.NotFound)
                await EnsureSuccessAsync(response, $"Das Release {release.TagName} konnte nicht gelöscht werden");
        }

        using (var response = await SendAsync(HttpMethod.Delete, repo, $"git/refs/tags/{Uri.EscapeDataString(release.TagName)}"))
        {
            // 422: Den Tag gab es schon nicht mehr - dann ist das Ziel erreicht.
            if (response.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity))
                await EnsureSuccessAsync(response, $"Der Tag {release.TagName} konnte nicht gelöscht werden");
        }

        Forget(repo);
    }

    /// <summary>
    /// Holt die kurzlebige Download-Adresse eines Anhangs aus dem privaten
    /// Repository. GitHub antwortet mit einer Umleitung auf eine signierte
    /// Adresse, die einige Minuten ohne Anmeldung gilt - das Token selbst
    /// verlässt den Server nie, und die Datei fliesst nicht durch ihn hindurch.
    /// </summary>
    public async Task<string> SignedDownloadUrlAsync(string repo, long assetId)
    {
        RequireToken();

        using var request = CreateRequest(HttpMethod.Get, repo, $"releases/assets/{assetId}");
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

        using var response = await httpFactory.CreateClient(nameof(GitHubService))
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            return location.ToString();

        throw new GitHubException($"GitHub gab keine Download-Adresse heraus ({(int)response.StatusCode}).");
    }

    public void Forget(string repo)
    {
        cache.Remove(CacheKey("releases", repo));
        cache.Remove(CacheKey("runs", repo));
    }

    private async Task<T?> GetAsync<T>(string repo, string path) where T : class
    {
        using var response = await SendAsync(HttpMethod.Get, repo, path);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        await EnsureSuccessAsync(response, "GitHub konnte nicht abgefragt werden");
        return await response.Content.ReadFromJsonAsync<T>();
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string repo, string path, HttpContent? content = null)
    {
        var request = CreateRequest(method, repo, path);
        request.Content = content;

        return httpFactory.CreateClient(nameof(GitHubService)).SendAsync(request);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string repo, string path)
    {
        var request = new HttpRequestMessage(method, $"https://api.github.com/repos/{settings.Owner}/{repo}/{path}");

        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("SchoolManager-Server", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        if (HasToken)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);

        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string what)
    {
        if (response.IsSuccessStatusCode)
            return;

        var detail = await response.Content.ReadAsStringAsync();

        if (detail.Length > 300)
            detail = detail[..300];

        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new GitHubException($"{what}: Das GitHub-Token gilt nicht (mehr)."),
            HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests =>
                new GitHubException($"{what}: GitHub verweigert den Zugriff - fehlt dem Token ein Recht, oder ist das Anfragelimit erreicht? {detail}"),
            _ => new GitHubException($"{what}: GitHub antwortete mit {(int)response.StatusCode}. {detail}")
        };
    }

    private void RequireToken()
    {
        if (!HasToken)
            throw new GitHubException("Auf dem Server ist kein GitHub-Token hinterlegt (GitHub__Token).");
    }

    private static string CacheKey(string kind, string repo) => $"github:{kind}:{repo}";

    [GeneratedRegex(@"<Version>\s*([0-9]+\.[0-9]+\.[0-9]+)\s*</Version>")]
    private static partial Regex VersionElement();

    private sealed record RunPage([property: JsonPropertyName("workflow_runs")] List<GitHubRun> WorkflowRuns);

    private sealed record CommitResponse(
        [property: JsonPropertyName("sha")] string Sha,
        [property: JsonPropertyName("commit")] CommitDetail Commit);

    private sealed record CommitDetail(
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("committer")] CommitPerson? Committer);

    private sealed record CommitPerson([property: JsonPropertyName("date")] DateTimeOffset? Date);

    private sealed record ContentResponse([property: JsonPropertyName("content")] string? Content);
}
