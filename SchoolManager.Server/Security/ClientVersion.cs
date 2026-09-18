using System.Net.Http.Headers;

namespace SchoolManager.Server.Security;

/// <summary>
/// Welche Fassung von School Manager fragt? Dev-Versionen gibt es erst ab
/// 1.2.0 - der ersten Fassung mit Anmeldung. Ältere Fassungen bekommen vom
/// Server nur öffentliche Releases, auch wenn sie irgendwie ein Token hätten.
/// </summary>
public static class ClientVersion
{
    /// <summary>Ab dieser Version darf eine App Dev-Versionen beziehen.</summary>
    public static readonly Version MinimumForDevVersions = new(1, 2, 0);

    /// <summary>Die Version aus dem User-Agent „SchoolManager/1.2.0“; null, wenn sie fehlt.</summary>
    public static Version? Of(HttpContext context)
    {
        foreach (var value in context.Request.Headers.UserAgent)
        {
            if (value is null)
                continue;

            foreach (var part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (ProductInfoHeaderValue.TryParse(part, out var product)
                    && product.Product is { Name: "SchoolManager", Version: { } text }
                    && Version.TryParse(text, out var version))
                    return version;
            }
        }

        return null;
    }

    /// <summary>
    /// Darf diese App Dev-Versionen sehen? Die Version kommt aus dem User-Agent
    /// und, wo die App sie ausdrücklich mitschickt, aus der Anfrage selbst - es
    /// gilt die kleinere.
    /// </summary>
    public static bool MayUseDevVersions(HttpContext context, Version? reported = null)
    {
        var fromHeader = Of(context);

        if (fromHeader is null)
            return false;

        var version = reported is not null && reported < fromHeader ? reported : fromHeader;

        return version >= MinimumForDevVersions;
    }
}
