using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SchoolManager.App.Data;

/// <summary>
/// Die E-Mail-Vorlage: eine feste Begrüssung und ein fester Abschluss, die jede
/// Nachricht beim Senden einrahmen. Beide dürfen Variablen in geschweiften
/// Klammern enthalten - eingebaute wie {Empfänger} und selbst festgelegte.
///
/// Eigene kleine Datei im Datenordner, unabhängig vom Postausgang.
/// </summary>
public static class MailTemplate
{
    private static readonly string FilePath = LocalStore.PathFor("email-vorlage.json");

    private static State state = Load();

    /// <summary>Meldet jede Änderung, damit die E-Mail-Seite nachziehen kann.</summary>
    public static event Action? Changed;

    public static bool Enabled => state.Enabled;

    public static string Greeting => state.Greeting;

    public static string Closing => state.Closing;

    /// <summary>Eigene Variablen, eine je Zeile als "Name = Wert".</summary>
    public static string Variables => state.Variables;

    public static void Update(bool enabled, string greeting, string closing, string variables)
    {
        state.Enabled = enabled;
        state.Greeting = greeting;
        state.Closing = closing;
        state.Variables = variables;
        Save();
    }

    /// <summary>Rahmt die Nachricht mit der gespeicherten Vorlage ein.</summary>
    public static string Apply(string body, string subject, string recipientName, bool html) =>
        Compose(Greeting, Closing, Variables, body, subject, recipientName, html);

    /// <summary>Setzt Begrüssung, Nachricht und Abschluss zusammen und füllt die Variablen.</summary>
    public static string Compose(string greeting, string closing, string variables,
        string body, string subject, string recipientName, bool html)
    {
        var values = Values(variables, subject, recipientName);
        var head = Fill(greeting, values);
        var foot = Fill(closing, values);

        // Die Nachricht selbst ist bei HTML schon HTML; nur die Vorlage wird umgewandelt.
        if (html)
        {
            head = ToHtml(head);
            foot = ToHtml(foot);
        }

        var separator = html ? "<br><br>" : Environment.NewLine + Environment.NewLine;

        return string.Join(separator, new[] { head, body.Trim('\r', '\n'), foot }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static Dictionary<string, string> Values(string variables, string subject, string recipientName)
    {
        var name = recipientName.Trim();
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Empfänger"] = name,
            ["Vorname"] = words.Length > 0 ? words[0] : "",
            ["Nachname"] = words.Length > 1 ? words[^1] : "",
            ["Betreff"] = subject.Trim(),
            ["Datum"] = DateTime.Today.ToString("dd.MM.yyyy")
        };

        foreach (var line in variables.Split('\n'))
        {
            var separator = line.IndexOf('=');

            if (separator <= 0)
                continue;

            var key = line[..separator].Trim().Trim('{', '}').Trim();

            if (key.Length > 0)
                values[key] = line[(separator + 1)..].Trim();
        }

        return values;
    }

    private static string Fill(string text, IReadOnlyDictionary<string, string> values)
    {
        var filled = Regex.Replace(text, @"\{([^{}\r\n]+)\}",
            match => values.TryGetValue(match.Groups[1].Value.Trim(), out var value) ? value : match.Value);

        // Fehlt etwa der Name, soll aus "Guten Tag {Empfänger}," kein "Guten Tag ," werden.
        filled = Regex.Replace(filled, @"[ \t]+([,.!?;:])", "$1");
        filled = Regex.Replace(filled, @"[ \t]{2,}", " ");

        var lines = filled.Replace("\r\n", "\n").Split('\n').Select(line => line.TrimEnd());

        return string.Join(Environment.NewLine, lines).Trim('\r', '\n');
    }

    private static string ToHtml(string text) =>
        WebUtility.HtmlEncode(text).Replace(Environment.NewLine, "<br>");

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(LocalStore.Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(state));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nicht speicherbar: dann gilt die Vorlage eben nur diesmal.
        }

        Changed?.Invoke();
    }

    private static State Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath)) ?? new State()
                : new State();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new State();
        }
    }

    private sealed class State
    {
        /// <summary>Voreinstellung aus - bis jemand die Vorlage bewusst einschaltet, bleibt alles wie bisher.</summary>
        public bool Enabled { get; set; }

        public string Greeting { get; set; } = "Guten Tag {Empfänger},";

        public string Closing { get; set; } = "Freundliche Grüsse" + Environment.NewLine + "{MeinName}";

        public string Variables { get; set; } = "MeinName = ";
    }
}
