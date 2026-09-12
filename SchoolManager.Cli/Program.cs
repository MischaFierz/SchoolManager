using SchoolManager.Cli;
using SchoolManager.Core;
using Microsoft.Extensions.Configuration;

try
{
    var options = CommandLineOptions.Parse(args);

    if (options.ShowHelp)
    {
        Console.WriteLine(CommandLineOptions.HelpText);
        return 0;
    }

    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .AddUserSecrets(typeof(CommandLineOptions).Assembly, optional: true)
        .AddEnvironmentVariables()
        .Build();

    var settings = configuration.GetSection("Smtp").Get<SmtpSettings>() ?? new SmtpSettings();

    // Microsoft 365 verlangt eine Anmeldung im Browser; das Token dafür liegt im
    // Benutzerprofil und wird von der Anwendung angelegt, nicht von hier. Ohne
    // diesen Hinweis lautete die Meldung "bitte in den Einstellungen anmelden" -
    // ein Rat, der auf der Kommandozeile nirgends hinführt.
    if (settings.UsesOAuth)
        throw new InvalidOperationException(
            "Die Konto-Art Microsoft 365 lässt sich hier nicht verwenden: Sie braucht eine "
            + "Anmeldung im Browser, die nur School Manager selbst durchführen kann. "
            + "Für den Versand aus Skripten ist in appsettings.json ein SMTP-Konto "
            + "einzutragen (AccountKind \"Smtp\" mit Host, Port, Benutzername und Passwort).");

    settings.Validate();

    var mail = options.Mail;

    // Was auf der Kommandozeile fehlt, wird hier nachgefragt.
    if (mail.To.Count == 0)
        mail.To.AddRange(CommandLineOptions.SplitAddresses(AskRequired("Empfänger")));

    mail.Subject = options.Subject ?? Ask("Betreff");
    mail.Body = options.Body ?? Ask("Nachricht");

    Console.WriteLine();
    Console.WriteLine($"Von     : {settings.EffectiveFrom}");
    Console.WriteLine($"An      : {string.Join(", ", mail.To)}");
    Console.WriteLine($"Betreff : {mail.Subject}");
    Console.WriteLine($"Server  : {settings.Host}:{settings.Port}");
    if (mail.Attachments.Count > 0)
        Console.WriteLine($"Anhänge : {string.Join(", ", mail.Attachments)}");
    Console.WriteLine();

    await new EmailService(settings).SendAsync(mail);

    Console.WriteLine("E-Mail wurde versendet.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Fehler: {ex.Message}");
    return 1;
}

static string Ask(string label)
{
    Console.Write($"{label}: ");
    return Console.ReadLine() ?? "";
}

static string AskRequired(string label)
{
    while (true)
    {
        var value = Ask(label);
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        Console.WriteLine($"{label} darf nicht leer sein.");
    }
}
