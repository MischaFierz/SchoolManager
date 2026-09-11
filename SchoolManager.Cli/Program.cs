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
