using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SchoolManager.Core;

/// <summary>
/// Verschickt E-Mails über Microsoft Graph statt über SMTP.
///
/// Das ist für Microsoft 365 der verlässlichere Weg: Exchange Online hat die
/// SMTP-Anmeldung seit 2020 standardmässig abgeschaltet, und viele Schulen
/// lassen sie abgeschaltet. Graph braucht sie nicht - es genügt die Anmeldung im
/// Browser mit den Berechtigungen aus <see cref="SmtpSettings.Microsoft365Scopes"/>:
/// Mail.Send fürs Verschicken, User.Read für <see cref="TestConnectionAsync"/> und
/// <see cref="GetMailboxAsync"/>, die beide das eigene Konto abfragen.
///
/// Die versendete Nachricht landet automatisch im Ordner "Gesendete Elemente"
/// des Postfachs; darum kümmert sich Microsoft, nicht diese Klasse.
/// </summary>
public sealed class GraphMailService(IAccessTokenSource tokenSource)
{
    private const string SendMailUrl = "https://graph.microsoft.com/v1.0/me/sendMail";

    /// <summary>
    /// Anhänge gehen als Base64 mit der Nachricht mit. Graph nimmt so
    /// höchstens 3 MB je Nachricht an; alles darüber bräuchte eine
    /// Hochlade-Sitzung, was hier absichtlich nicht gebaut ist.
    /// </summary>
    private const long AttachmentLimit = 3L * 1024 * 1024;

    public async Task SendAsync(SmtpSettings settings, OutgoingEmail mail,
        CancellationToken cancellationToken = default)
    {
        var message = BuildMessage(settings, mail);
        await AddAttachmentsAsync(message, mail, cancellationToken);

        var payload = new JsonObject
        {
            ["message"] = message,
            // Die Nachricht wird im Postfach abgelegt, wie bei Outlook auch.
            ["saveToSentItems"] = true
        };

        using var http = await CreateClientAsync(settings, cancellationToken);
        using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(SendMailUrl, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                await DescribeFailureAsync(response, cancellationToken));
    }

    /// <summary>
    /// Prüft Anmeldung und Postfach, ohne etwas zu senden: ein Blick auf das
    /// eigene Konto genügt, um zu sehen, dass das Token stimmt.
    /// </summary>
    public async Task TestConnectionAsync(SmtpSettings settings, CancellationToken cancellationToken = default)
    {
        using var http = await CreateClientAsync(settings, cancellationToken);
        using var response = await http.GetAsync(
            "https://graph.microsoft.com/v1.0/me?$select=mail,userPrincipalName", cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                await DescribeFailureAsync(response, cancellationToken));
    }

    /// <summary>Die Adresse des angemeldeten Postfachs, für die Anzeige.</summary>
    public async Task<string> GetMailboxAsync(SmtpSettings settings, CancellationToken cancellationToken = default)
    {
        using var http = await CreateClientAsync(settings, cancellationToken);
        using var response = await http.GetAsync(
            "https://graph.microsoft.com/v1.0/me?$select=mail,userPrincipalName,displayName", cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var root = document.RootElement;

        return Text(root, "mail") is { Length: > 0 } mailbox
            ? mailbox
            : Text(root, "userPrincipalName") ?? "";

        static string? Text(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private async Task<HttpClient> CreateClientAsync(SmtpSettings settings, CancellationToken cancellationToken)
    {
        var token = await tokenSource.GetAccessTokenAsync(settings.UserName, cancellationToken);

        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return http;
    }

    private static JsonObject BuildMessage(SmtpSettings settings, OutgoingEmail mail)
    {
        var to = Recipients(mail.To);
        var cc = Recipients(mail.Cc);
        var bcc = Recipients(mail.Bcc);

        if (to.Count + cc.Count + bcc.Count == 0)
            throw new InvalidOperationException("Es wurde kein Empfänger angegeben.");

        var message = new JsonObject
        {
            ["subject"] = mail.Subject,
            ["body"] = new JsonObject
            {
                ["contentType"] = mail.IsHtml ? "HTML" : "Text",
                ["content"] = mail.Body
            },
            ["toRecipients"] = to,
            ["ccRecipients"] = cc,
            ["bccRecipients"] = bcc
        };

        // Ein abweichender Absender geht nur, wenn das Postfach die Berechtigung
        // "Senden als" hat; ohne Angabe nimmt Graph das angemeldete Postfach.
        if (!string.IsNullOrWhiteSpace(settings.FromAddress) &&
            !settings.FromAddress.Equals(settings.UserName, StringComparison.OrdinalIgnoreCase))
        {
            message["from"] = Recipient(settings.FromAddress, settings.FromDisplayName);
        }

        return message;
    }

    private static JsonArray Recipients(IEnumerable<string> addresses)
    {
        var list = new JsonArray();

        foreach (var address in addresses)
        {
            if (string.IsNullOrWhiteSpace(address))
                continue;

            var trimmed = address.Trim();

            // Dieselbe Prüfung wie beim SMTP-Versand, damit ein Tippfehler hier
            // auffällt und nicht erst als Fehlermeldung von Microsoft.
            if (!MimeKit.MailboxAddress.TryParse(trimmed, out var parsed))
                throw new InvalidOperationException($"'{trimmed}' ist keine gültige E-Mail-Adresse.");

            list.Add(Recipient(parsed.Address, parsed.Name));
        }

        return list;
    }

    private static JsonObject Recipient(string address, string? name)
    {
        var emailAddress = new JsonObject { ["address"] = address };

        if (!string.IsNullOrWhiteSpace(name))
            emailAddress["name"] = name;

        return new JsonObject { ["emailAddress"] = emailAddress };
    }

    private static async Task AddAttachmentsAsync(JsonObject message, OutgoingEmail mail,
        CancellationToken cancellationToken)
    {
        var attachments = new JsonArray();
        var total = 0L;

        foreach (var path in mail.Attachments)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var fullPath = Path.GetFullPath(path);

            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"Anhang nicht gefunden: {path}", fullPath);

            var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
            total += bytes.LongLength;

            if (total > AttachmentLimit)
                throw new InvalidOperationException(
                    "Die Anhänge sind zusammen grösser als 3 MB. Microsoft 365 nimmt so viel auf einmal "
                    + "nicht an - bitte weniger anhängen oder die Datei über OneDrive teilen.");

            attachments.Add(new JsonObject
            {
                ["@odata.type"] = "#microsoft.graph.fileAttachment",
                ["name"] = Path.GetFileName(fullPath),
                ["contentBytes"] = System.Convert.ToBase64String(bytes)
            });
        }

        if (attachments.Count > 0)
            message["attachments"] = attachments;
    }

    /// <summary>
    /// Macht aus der Antwort von Graph eine Meldung, mit der sich etwas
    /// anfangen lässt - die häufigen Fälle im Klartext.
    /// </summary>
    private static async Task<string> DescribeFailureAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var detail = "";

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var text))
                detail = text.GetString() ?? "";
        }
        catch (JsonException)
        {
            detail = body.Length > 300 ? body[..300] : body;
        }

        var hint = (int)response.StatusCode switch
        {
            401 => "Die Anmeldung ist abgelaufen - bitte in den Einstellungen neu anmelden.",
            403 => "Das Konto darf über diese App nicht senden. Meist fehlt die Zustimmung der "
                   + "Schul-Administration zur Berechtigung Mail.Send.",
            404 => "Für dieses Konto gibt es kein Postfach in Microsoft 365.",
            413 => "Die Nachricht ist zu gross.",
            429 => "Microsoft bremst gerade ab (zu viele Anfragen) - später nochmals versuchen.",
            _ => ""
        };

        return string.Join(" ", new[] { hint, detail }.Where(part => part.Length > 0)) is { Length: > 0 } message
            ? message
            : $"Microsoft 365 hat den Versand abgelehnt ({(int)response.StatusCode} {response.ReasonPhrase}).";
    }
}
