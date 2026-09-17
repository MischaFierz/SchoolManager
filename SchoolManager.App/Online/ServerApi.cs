using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SchoolManager.App.Update;

namespace SchoolManager.App.Online;

/// <summary>Das Konto, mit dem der Entwicklermodus angemeldet ist.</summary>
public sealed record DevAccount(
    string UserName,
    string DisplayName,
    string Level,
    string[] Permissions,
    string[] Groups,
    bool PublicOnly)
{
    /// <summary>Anzeige wie „Mischa (admin) · Administrator“.</summary>
    public string Label => DisplayName.Length > 0 ? $"{DisplayName} ({UserName}) · {Level}" : $"{UserName} · {Level}";
}

/// <summary>Eine Meldung aus dem Admin-Panel für den Streifen oben im Fenster.</summary>
/// <param name="Revision">Ändert sich mit jeder Bearbeitung - eine weggeklickte, danach geänderte Meldung erscheint wieder.</param>
/// <param name="Page">Die Seite der App, auf der sie erscheint, etwa "calendar"; leer heisst: auf jeder Seite.</param>
public sealed record ServerMessage(int Id, string Text, string Kind, long Revision, string? Page = null)
{
    public bool IsError => Kind == "Error";

    /// <summary>Gehört die Meldung auf diese Seite?</summary>
    public bool BelongsTo(string pageKey) => string.IsNullOrEmpty(Page) || Page == pageKey;

    /// <summary>Unter diesem Schlüssel merkt sich die App, dass sie weggeklickt wurde.</summary>
    public string Key => $"{Id}:{Revision}";
}

/// <summary>Der Server hat abgelehnt oder war nicht zu erreichen.</summary>
public sealed class ServerException(string message, HttpStatusCode? status = null) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;

    /// <summary>Gar keine Antwort - kein Netz, Server aus oder falsche Adresse.</summary>
    public bool IsUnreachable => Status is null;
}

/// <summary>
/// Spricht mit dem School-Manager-Server: Anmeldung für den Entwicklermodus,
/// Meldungen aus dem Admin-Panel und die Update-Suche mit Freigaben.
///
/// Die Adresse kommt beim Bauen hinein (<c>-p:ServerUrl=…</c>); eine Fassung
/// ohne Adresse arbeitet wie früher allein mit GitHub und kennt keinen
/// Entwicklermodus.
/// </summary>
public static class ServerApi
{
    private static readonly HttpClient Http = CreateClient();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Die Adresse des Servers ohne Schrägstrich am Ende; leer, wenn keine eingebaut ist.</summary>
    public static string BaseUrl { get; } =
        (Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "ServerUrl")?.Value ?? "").TrimEnd('/');

    public static bool IsConfigured => BaseUrl.Length > 0;

    public static async Task<(string Token, DevAccount Account)> SignInAsync(string userName, string password)
    {
        var result = await SendAsync<LoginResponse>(HttpMethod.Post, "/api/auth/login", null,
            new { userName, password, client = "app" });

        return (result!.Token, result.User);
    }

    /// <summary>Das Konto hinter einer Anmeldung; null, wenn sie nicht mehr gilt.</summary>
    public static async Task<DevAccount?> AccountAsync(string token)
    {
        try
        {
            return await SendAsync<DevAccount>(HttpMethod.Get, "/api/me", token);
        }
        catch (ServerException ex) when (ex.Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return null;
        }
    }

    /// <summary>Meldet beim Server ab. Klappt das nicht, läuft die Anmeldung dort eben von selbst ab.</summary>
    public static async Task SignOutAsync(string token)
    {
        try
        {
            await SendAsync<object>(HttpMethod.Post, "/api/auth/logout", token);
        }
        catch (ServerException)
        {
        }
    }

    public static async Task<IReadOnlyList<ServerMessage>> MessagesAsync(string? token) =>
        await SendAsync<List<ServerMessage>>(HttpMethod.Get, "/api/app/messages", token) ?? [];

    /// <summary>Die neueste Version, die dieses Konto beziehen darf und die neuer ist als die laufende.</summary>
    public static async Task<UpdateInfo?> UpdateAsync(Version current, bool dev, string? token)
    {
        var response = await SendAsync<UpdateResponse>(HttpMethod.Get,
            $"/api/app/update?current={current.ToString(3)}&dev={(dev ? "true" : "false")}", token);

        return response?.Update?.ToInfo();
    }

    /// <summary>Alle Versionen, die dieses Konto beziehen darf, die neueste zuerst.</summary>
    public static async Task<IReadOnlyList<UpdateInfo>> ReleasesAsync(string token) =>
        (await SendAsync<List<ReleaseDto>>(HttpMethod.Get, "/api/app/releases", token) ?? [])
        .Select(release => release.ToInfo())
        .ToList();

    /// <summary>
    /// Die Download-Adresse einer Dev-Version. Sie ist signiert und gilt nur
    /// wenige Minuten - darum erst unmittelbar vor dem Herunterladen holen.
    /// </summary>
    public static async Task<string> DownloadUrlAsync(string tag, string token) =>
        (await SendAsync<DownloadResponse>(HttpMethod.Get, $"/api/app/download/{Uri.EscapeDataString(tag)}", token))!.Url;

    private static async Task<T?> SendAsync<T>(HttpMethod method, string path, string? token, object? body = null) where T : class
    {
        if (!IsConfigured)
            throw new ServerException("In dieser Fassung ist kein Server eingetragen.");

        using var request = new HttpRequestMessage(method, BaseUrl + path);

        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (body is not null)
            request.Content = JsonContent.Create(body, options: Json);

        HttpResponseMessage response;

        try
        {
            response = await Http.SendAsync(request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ServerException("Der School-Manager-Server ist nicht erreichbar.");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new ServerException(ErrorText(text) ?? $"Der Server antwortete mit {(int)response.StatusCode}.", response.StatusCode);

            if (text.Length == 0)
                return null;

            try
            {
                return JsonSerializer.Deserialize<T>(text, Json);
            }
            catch (JsonException)
            {
                throw new ServerException("Die Antwort des Servers war unlesbar.", response.StatusCode);
            }
        }
    }

    private static string? ErrorText(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<ErrorResponse>(body, Json)?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SchoolManager", UpdateService.CurrentVersion.ToString(3)));
        return http;
    }

    private sealed record LoginResponse(string Token, DevAccount User);

    private sealed record ErrorResponse(string? Error);

    private sealed record DownloadResponse(string Url);

    private sealed record UpdateResponse(ReleaseDto? Update);

    private sealed record ReleaseDto(
        string Version,
        string Tag,
        bool IsDev,
        long Size,
        string ReleaseUrl,
        string DownloadUrl,
        string Note)
    {
        public UpdateInfo ToInfo() => new(Version, DownloadUrl, ReleaseUrl, Size, IsDev, Tag, Note ?? "");
    }
}
