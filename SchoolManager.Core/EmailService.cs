using MailKit.Net.Smtp;
using MimeKit;

namespace SchoolManager.Core;

/// <summary>
/// Versendet E-Mails. Für gewöhnliche Konten über SMTP mit Benutzername und
/// Passwort; Microsoft 365 geht stattdessen über Microsoft Graph, wofür
/// <paramref name="tokenSource"/> die Anmeldung liefert.
/// </summary>
public sealed class EmailService(SmtpSettings settings, IAccessTokenSource? tokenSource = null)
{
    public async Task SendAsync(OutgoingEmail mail, CancellationToken cancellationToken = default)
    {
        settings.Validate();

        // Microsoft 365 geht über Graph, nicht über SMTP: Exchange Online hat
        // die SMTP-Anmeldung standardmässig abgeschaltet.
        if (settings.UsesOAuth)
        {
            await Graph().SendAsync(settings, mail, cancellationToken);
            return;
        }

        var message = BuildMessage(mail);
        await using var attachmentStreams = new StreamBag();
        await AddAttachmentsAsync(message, mail, attachmentStreams, cancellationToken);

        using var client = new SmtpClient();
        await ConnectAsync(client, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    /// <summary>
    /// Baut die Verbindung auf und meldet sich an, ohne etwas zu senden.
    /// Schlägt fehl, wenn Server, Port oder Zugangsdaten nicht stimmen.
    /// </summary>
    public async Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        settings.Validate();

        if (settings.UsesOAuth)
        {
            await Graph().TestConnectionAsync(settings, cancellationToken);
            return;
        }

        using var client = new SmtpClient();
        await ConnectAsync(client, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    /// <summary>Der Versandweg für Microsoft 365; braucht die Anmeldung.</summary>
    private GraphMailService Graph() =>
        tokenSource is null
            ? throw new InvalidOperationException(
                "Für Microsoft 365 fehlt die Anmeldung - bitte in den Einstellungen anmelden.")
            : new GraphMailService(tokenSource);

    private async Task ConnectAsync(SmtpClient client, CancellationToken cancellationToken)
    {
        await client.ConnectAsync(settings.Host, settings.Port, settings.SocketOptions, cancellationToken);

        // Hier kommt nur an, wer nicht über Graph geht: Microsoft 365 ist oben
        // schon abgezweigt. Eine SMTP-Anmeldung per Zugriffstoken braucht es
        // darum nicht - Exchange Online liesse sie ohnehin meist nicht zu.
        if (!string.IsNullOrWhiteSpace(settings.UserName))
            await client.AuthenticateAsync(settings.UserName, settings.Password, cancellationToken);
    }

    private MimeMessage BuildMessage(OutgoingEmail mail)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromDisplayName, settings.EffectiveFrom));

        AddRecipients(message.To, mail.To);
        AddRecipients(message.Cc, mail.Cc);
        AddRecipients(message.Bcc, mail.Bcc);

        if (message.To.Count + message.Cc.Count + message.Bcc.Count == 0)
            throw new InvalidOperationException("Es wurde kein Empfänger angegeben.");

        message.Subject = mail.Subject;
        return message;
    }

    private static void AddRecipients(InternetAddressList list, IEnumerable<string> addresses)
    {
        foreach (var address in addresses)
        {
            if (string.IsNullOrWhiteSpace(address))
                continue;

            var trimmed = address.Trim();

            if (!MailboxAddress.TryParse(trimmed, out var parsed))
                throw new InvalidOperationException($"'{trimmed}' ist keine gültige E-Mail-Adresse.");

            list.Add(parsed);
        }
    }

    private static async Task AddAttachmentsAsync(
        MimeMessage message,
        OutgoingEmail mail,
        StreamBag streams,
        CancellationToken cancellationToken)
    {
        var builder = new BodyBuilder();

        if (mail.IsHtml)
            builder.HtmlBody = mail.Body;
        else
            builder.TextBody = mail.Body;

        foreach (var path in mail.Attachments)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            // GetFullPath normalisiert Schrägstriche, damit im Mail nur der
            // Dateiname und nicht der ganze Pfad als Anhangsname landet.
            var fullPath = Path.GetFullPath(path);

            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"Anhang nicht gefunden: {path}", fullPath);

            // Der Stream muss bis zum Versand offen bleiben, darum die StreamBag.
            var stream = File.OpenRead(fullPath);
            streams.Add(stream);
            await builder.Attachments.AddAsync(Path.GetFileName(fullPath), stream, cancellationToken);
        }

        message.Body = builder.ToMessageBody();
    }

    /// <summary>Hält geöffnete Anhang-Streams, bis die Nachricht verschickt ist.</summary>
    private sealed class StreamBag : IAsyncDisposable
    {
        private readonly List<Stream> streams = [];

        public void Add(Stream stream) => streams.Add(stream);

        public async ValueTask DisposeAsync()
        {
            foreach (var stream in streams)
                await stream.DisposeAsync();
        }
    }
}
