namespace SchoolManager.App.Notifications;

/// <summary>
/// Eine Sache, die noch offen ist: überfällig oder in den nächsten Tagen
/// fällig. Woher sie stammt - Auftrag, Aufgabe, Hausaufgabe, Prüfung, To-Do -
/// steht in <see cref="Kind"/>.
/// </summary>
/// <param name="Kind">Art des Eintrags, etwa "Hausaufgabe".</param>
/// <param name="Title">Was es ist, etwa "Mathematik: Kapitel 3".</param>
/// <param name="Due">Wann es fällig ist; null bei Dingen ohne Datum.</param>
public sealed record Reminder(string Kind, string Title, DateTimeOffset? Due)
{
    /// <summary>Termin überschritten.</summary>
    public bool IsOverdue => Due is { } due && due.LocalDateTime.Date < DateTime.Today;

    /// <summary>Wie viele Tage es noch sind; negativ, wenn es vorbei ist.</summary>
    public int DaysLeft => Due is { } due ? (due.LocalDateTime.Date - DateTime.Today).Days : int.MaxValue;

    /// <summary>Der Termin in Worten.</summary>
    public string WhenText => DaysLeft switch
    {
        int.MaxValue => "ohne Termin",
        0 => "heute",
        1 => "morgen",
        -1 => "seit gestern überfällig",
        < 0 => $"{-DaysLeft} Tage überfällig",
        _ => $"in {DaysLeft} Tagen"
    };

    /// <summary>Eine Zeile für die Sprechblase, etwa "Hausaufgabe: Mathe - morgen".</summary>
    public string Line => $"{Kind}: {Title} - {WhenText}";
}
