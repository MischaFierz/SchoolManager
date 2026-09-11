namespace SchoolManager.App.Logging;

/// <summary>
/// Was sich von Fassung zu Fassung getan hat.
///
/// Der Entwicklerbereich zeigt daraus alles, was seit der letzten öffentlichen
/// Version dazugekommen ist - also genau das, was an einer Vorabversion neu
/// ist und noch niemand sonst hat.
///
/// **Beim Vorbereiten einer Vorabversion gehört hier ein Eintrag dazu.** Die
/// Liste wird von Hand gepflegt und zusammen mit der Änderung festgehalten;
/// nur so steht im fertigen Programm, was darin steckt.
/// </summary>
public static class Changelog
{
    /// <summary>Eine Fassung mit dem, was sie gebracht hat.</summary>
    /// <param name="Version">Die Nummer, etwa "1.1.1".</param>
    /// <param name="Date">Das Datum als tt.mm.jjjj.</param>
    /// <param name="IsPreRelease">Vorabversion - nur im Entwicklermodus zu haben.</param>
    /// <param name="Changes">Die einzelnen Punkte.</param>
    public sealed record Entry(string Version, string Date, bool IsPreRelease, string[] Changes)
    {
        /// <summary>Überschrift im Changelog, etwa "1.1.1 · Vorabversion · 11.09.2026".</summary>
        public string Header => IsPreRelease
            ? $"{Version} · Vorabversion · {Date}"
            : $"{Version} · öffentlich · {Date}";
    }

    /// <summary>Die Fassungen, die neueste zuoberst.</summary>
    public static IReadOnlyList<Entry> All { get; } =
    [
        new("1.1.1", "11.09.2026", true,
        [
            "Die Einstellungen liessen sich nur ein Stück weit blättern: Sobald der Zeiger über einem Auswahlfeld stand, verschluckte dieses das Mausrad. Das Rad wird jetzt an die Seite weitergereicht - auch auf den Seiten Aufträge, Aufgaben, Hausaufgaben, Prüfungen und Lehrkräfte.",
            "Neuer Bereich „Protokoll“ im Entwicklermodus: Der Reiter „Fehler“ sammelt alle Fehlermeldungen der Anwendung samt Abstürzen, der Reiter „Changelog“ zeigt, was diese Vorabversion seit der letzten öffentlichen Version enthält.",
            "Fehlermeldungen landen zusätzlich in der Datei fehler.log im Datenordner und überleben damit auch einen Absturz."
        ]),

        new("1.1.0", "11.09.2026", false,
        [
            "Erinnerungen an alles Offene - überfällige und bald fällige Aufträge, Aufgaben, Hausaufgaben, Prüfungen, Termine und To-Dos - als Sprechblase beim Symbol neben der Uhr, auch ohne geöffnetes Fenster.",
            "Autostart beim Anmelden und ein Schalter in den Einstellungen, ob erinnert werden soll; beim ersten Start wird danach gefragt.",
            "Notizen und To-Dos lassen sich einem Fach zuordnen, To-Dos zusätzlich mit einem Datum versehen (bewusst ohne Eintrag im Kalender).",
            "Alle Datumsfelder haben ein kleines Monatsblatt zum Auswählen statt reiner Tipperei.",
            "Der Hinweis auf ein Update erscheint im Fenster statt als eigenes Fenster unten rechts."
        ]),

        new("1.0.2", "11.09.2026", false,
        [
            "Der Rückweg von einer Entwicklerversion auf die öffentliche Version blieb wirkungslos, weil Deinstallation und Installation einander in die Quere kamen."
        ]),

        new("1.0.1", "11.09.2026", false,
        [
            "Überlappende Kalendereinträge waren unlesbar und nicht erreichbar; gestaffelte Karten kommen jetzt unter der Maus nach vorne.",
            "Der Entwicklermodus sichert beim Einschalten die Daten und bietet beim Verlassen den Weg zurück auf die öffentliche Version an.",
            "Vorabversionen wurden von der Update-Suche gar nicht gefunden, weil die Versionsnummer mit dem Zusatz „-dev“ nicht gelesen werden konnte."
        ])
    ];

    /// <summary>Die letzte öffentliche Fassung; daran misst sich eine Vorabversion.</summary>
    public static Entry? LastPublic => All.FirstOrDefault(entry => !entry.IsPreRelease);

    /// <summary>
    /// Alles, was seit der letzten öffentlichen Fassung dazugekommen ist -
    /// also die Vorabversionen davor in der Liste.
    /// </summary>
    public static IReadOnlyList<Entry> SinceLastPublic =>
        All.TakeWhile(entry => entry.IsPreRelease).ToList();
}
