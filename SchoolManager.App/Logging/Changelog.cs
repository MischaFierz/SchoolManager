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
        new("1.1.5", "13.09.2026", false,
        [
            "Dev-Patches kommen ab jetzt aus einem privaten Repository und sind öffentlich weder zu sehen noch herunterzuladen. Der Entwicklermodus lädt sie mit einem eingebauten Nur-Lese-Zugang; eine Fassung ohne diesen Zugang sieht schlicht keine Dev-Patches."
        ]),

        new("1.1.4", "13.09.2026", false,
        [
            "„Verbindung testen“ hätte bei Microsoft 365 nie funktionieren können: Geprüft wird über Microsoft Graph mit einem Blick auf das eigene Konto, angefordert wurde bei der Anmeldung aber nur die Berechtigung zum Senden (Mail.Send). Graph antwortete darauf mit „keine Berechtigung“. Die Anmeldung fragt jetzt zusätzlich User.Read - nötig nur zum Nachsehen, als wer man angemeldet ist, nicht zum Senden.",
            "Beim Prüfen eines Microsoft-365-Kontos stand in der Fussleiste „Verbindung zu smtp.office365.com:587 wird geprüft“, obwohl dort weder Server noch Port eine Rolle spielen. Jetzt steht dort, dass die Anmeldung geprüft wird, und bei Erfolg, über welches Postfach gesendet wird.",
            "Scheitert das Senden, die Anmeldung oder der Verbindungstest, steht im Protokoll unter „Fehler“ jetzt die ganze Ursachenkette statt nur der äussersten Meldung. MailKit und die Microsoft-Anmeldung verpacken den eigentlichen Grund regelmässig in einer inneren Ausnahme, die in der Fussleiste gar nicht vorkam.",
            "Der E-Mail-Versand ist freigeschaltet: In School Manager steckt jetzt eine Anwendungs-ID, darum genügt bei Microsoft 365 ein Klick auf „Mit Microsoft anmelden“ - kein Server, kein Passwort, keine eigene Azure-Registrierung mehr. Der rote Streifen „noch nicht verfügbar“ ist damit weg, und die Konto-Art steht allen offen statt nur dem Entwicklermodus. Beides hängt an der eingebauten ID, nicht an einem festen Schalter.",
            "Das Konsolen-Programm lief mit der Konto-Art Microsoft 365 in die Meldung „bitte in den Einstellungen anmelden“ - ein Rat, der auf der Kommandozeile nirgends hinführt, weil die Anmeldung einen Browser braucht. Jetzt bricht es gleich beim Laden ab und sagt, was stattdessen einzutragen ist.",
            "Aufgeräumt: Im Versand steckte noch eine SMTP-Anmeldung per Zugriffstoken, die seit dem Weg über Microsoft Graph nie mehr erreicht wurde.",
            "Die Anmeldung im Browser blieb bei Microsoft mit „AADSTS900971: No reply address provided“ stehen: In der eingebauten App-Registrierung fehlte die Antwortadresse. Neu gibt es „Mit Code anmelden“ - School Manager zeigt einen Code, legt ihn in die Zwischenablage und öffnet die Seite von Microsoft zum Eingeben; das braucht keine Antwortadresse. Die Anmeldung im Browser verlangt jetzt ausdrücklich http://localhost, genau die Adresse, die in der Registrierung stehen muss.",
            "Senden über Microsoft 365 scheiterte mit „does not have the right to send mail on behalf of the specified sending account“, sobald in den Einstellungen noch ein anderer Absender stand, etwa aus früheren SMTP-Einstellungen. Gesendet wird jetzt immer vom angemeldeten Postfach.",
            "Beim Einschalten des Entwicklermodus ist „Dev-Patches statt Releases beziehen“ gleich angekreuzt - vorher fand die Update-Suche danach nur das öffentliche Release. Jeder Wechsel des Kanals, auch das Abschalten, sucht sofort neu im passenden Kanal.",
            "Die Versionsnummer unten links bleibt im Entwicklermodus eingefärbt, zeigt „-dev“ aber nur noch, wenn wirklich ein Dev-Patch läuft.",
            "Der E-Mail-Versand läuft nur noch über die Anmeldung bei Microsoft: Konto-Auswahl, SMTP- und Exchange-Server sowie „Mit Code anmelden“ gibt es nur noch im Entwicklermodus. War bisher ein anderes Konto eingestellt, stellt School Manager auf Microsoft 365 um."
        ]),

        new("1.1.3", "13.09.2026", false,
        [
            "Ein Update konnte ins Leere laufen: Das Installationspaket startete, während School Manager noch lief und die eigene Programmdatei belegte. Jetzt wartet das Update, bis die App wirklich beendet ist, installiert mit einem Fortschrittsbalken und startet School Manager danach wieder - auch wenn die Installation scheitert, dann in der bisherigen Version und mit einer Meldung, warum.",
            "Beim Herunterladen eines Updates steht jetzt der Fortschritt in Prozent da. Ein unvollständig angekommenes Paket wird erkannt und nicht installiert; bleibt der Download stehen, bricht er nach 30 Sekunden mit einer Meldung ab.",
            "Während ein Update heruntergeladen wird, lässt sich der Entwicklermodus nicht verlassen und umgekehrt - das eine brach das andere sonst mittendrin ab.",
            "Konnte GitHub nicht abgefragt werden, hiess es trotzdem „Sie verwenden bereits die aktuellste Version“. Jetzt steht dort, dass die Suche fehlgeschlagen ist, und warum.",
            "Ein Installationspaket mit derselben Versionsnummer - etwa das öffentliche Release über dem gleichnamigen Dev-Patch - ersetzt die vorhandene Installation, statt sich als zweiter Eintrag daneben zu legen."
        ]),

        new("1.1.2", "11.09.2026", false,
        [
            "Bei den Prüfungen stand die Uhrzeit in derselben Zeile wie das Datum; seit das Datumsfeld den Kalender mitbringt, wurde die Zeile dafür zu lang. „Beginn“ hat jetzt eine eigene Zeile.",
            "Die Versionsnummer in den Einstellungen stand in Schwarz auf dunklem Grund und war kaum zu lesen."
        ]),

        new("1.1.1", "11.09.2026", false,
        [
            "Die Einstellungen liessen sich nur ein Stück weit blättern: Sobald der Zeiger über einem Auswahlfeld stand, verschluckte dieses das Mausrad. Das Rad wird jetzt an die Seite weitergereicht - auch auf den Seiten Aufträge, Aufgaben, Hausaufgaben, Prüfungen und Lehrkräfte.",
            "Neue Seite „Protokoll“ mit eigenem Punkt in der Navigation, sichtbar nur im Entwicklermodus: Der Reiter „Fehler“ sammelt alle Fehlermeldungen der Anwendung samt Abstürzen, der Reiter „Changelog“ zeigt, was diese Vorabversion seit der letzten öffentlichen Version enthält.",
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
