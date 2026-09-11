using SchoolManager.Core;

namespace SchoolManager.Cli;

/// <summary>Wertet die Kommandozeilen-Argumente aus.</summary>
public sealed class CommandLineOptions
{
    public OutgoingEmail Mail { get; } = new();
    public bool ShowHelp { get; set; }

    /// <summary>Null, solange die Angabe fehlt - dann wird interaktiv nachgefragt.</summary>
    public string? Subject { get; set; }

    public string? Body { get; set; }

    public static CommandLineOptions Parse(string[] args)
    {
        var options = new CommandLineOptions();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--to" or "-t":
                    options.Mail.To.AddRange(SplitAddresses(NextValue(args, ref i)));
                    break;

                case "--cc":
                    options.Mail.Cc.AddRange(SplitAddresses(NextValue(args, ref i)));
                    break;

                case "--bcc":
                    options.Mail.Bcc.AddRange(SplitAddresses(NextValue(args, ref i)));
                    break;

                case "--subject" or "-s":
                    options.Subject = NextValue(args, ref i);
                    break;

                case "--body" or "-b":
                    options.Body = NextValue(args, ref i);
                    break;

                case "--body-file" or "-f":
                    var path = NextValue(args, ref i);
                    if (!File.Exists(path))
                        throw new FileNotFoundException($"Textdatei nicht gefunden: {path}", path);
                    options.Body = File.ReadAllText(path);
                    break;

                case "--attach" or "-a":
                    options.Mail.Attachments.Add(NextValue(args, ref i));
                    break;

                case "--html":
                    options.Mail.IsHtml = true;
                    break;

                case "--help" or "-h" or "-?":
                    options.ShowHelp = true;
                    break;

                default:
                    throw new ArgumentException($"Unbekanntes Argument: {args[i]}");
            }
        }

        return options;
    }

    private static string NextValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"Zu '{args[index]}' fehlt der Wert.");

        return args[++index];
    }

    public static IEnumerable<string> SplitAddresses(string value) =>
        value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public const string HelpText = """
        School Manager CLI - verschickt eine E-Mail über SMTP.

        Verwendung:
          SchoolManager.Cli [Optionen]

        Optionen:
          -t, --to <adressen>     Empfänger, mehrere durch Komma getrennt
                                  (Option kann auch mehrfach angegeben werden)
              --cc <adressen>     Kopie an
              --bcc <adressen>    Blindkopie an
          -s, --subject <text>    Betreff
          -b, --body <text>       Nachrichtentext
          -f, --body-file <datei> Nachrichtentext aus einer Datei lesen
          -a, --attach <datei>    Anhang (mehrfach möglich)
              --html              Nachrichtentext als HTML senden
          -h, --help              Diese Hilfe anzeigen

        Fehlen Empfänger, Betreff oder Text, wird interaktiv nachgefragt.

        Beispiel:
          SchoolManager.Cli --to max@example.com --subject "Hallo" --body "Kurzer Test"

        Die SMTP-Zugangsdaten stehen im Abschnitt "Smtp" der appsettings.json.
        Das Passwort besser über User Secrets oder Umgebungsvariablen setzen:
          dotnet user-secrets set "Smtp:Password" "geheim"
          setx Smtp__Password "geheim"

        Wer es lieber grafisch mag, startet die Desktop-App SchoolManager.exe.
        """;
}
