# School Manager

Windows-Desktop-App für die Schule: Aufträge mit Aufgaben und Leistungsdetails
samt Zeitrechner, Hausaufgaben, Prüfungen, ein Kalender, der alles zusammen
zeigt und als ICS aus- und einlesen kann, Lehrkräfte mit Empfängerauswahl,
E-Mail-Versand (auch über Exchange), To-Do-Liste und Notizen.

**[⬇ Neueste Version herunterladen](https://github.com/MischaFierz/SchoolManager/releases/latest)**

## Installieren

`SchoolManagerSetup.msi` aus dem Release oben herunterladen und doppelklicken.
Das Paket installiert ohne Administratorrechte nach
`%LOCALAPPDATA%\Programs\School Manager`, legt eine Verknüpfung im Startmenü und
auf dem Desktop an und erscheint in „Apps & Features“ zum Deinstallieren. Die
.NET-Laufzeit ist enthalten, es muss nichts weiter installiert werden.

Wer die App nur kopieren möchte: `SchoolManager.exe` ist eine einzige Datei und
läuft von jedem Ort, auch von einem Stick.

## Die App

Links liegt die Seitennavigation: **Aufträge**, **Aufgaben**, **Hausaufgaben**,
**Prüfungen**, **Kalender**, **Lehrkräfte**, **E-Mail**, **To-Do**, **Notizen**
und, unten abgesetzt, **Einstellungen**. `Strg`+`1` bis `Strg`+`9` schalten
direkt um, `Strg`+`0` zu den Einstellungen. Unten quer läuft eine gemeinsame
Statuszeile: grau für Hinweise, grün für Erfolg, rot für Fehler.

Der E-Mail-Versand ist einsatzbereit; es braucht nur ein eingerichtetes Konto
(siehe „Einstellungen"). Fehlt es, sagt das die Statuszeile beim Start.

### Aufträge, Aufgaben, Leistungsdetails

Drei Ebenen, jede auf ihrer eigenen Seite:

* **Aufträge** — links die Liste, rechts der Auftrag mit Titel, Auftraggeber,
  Fach, Fertig-Haken, benötigter Zeit, Enddatum, Details und der Liste seiner
  Aufgaben. „Öffnen“ springt zur Aufgabe.
* **Aufgaben** — Übersicht über alle Aufgaben aller Aufträge. Rechts die
  Aufgabe und die Liste ihrer Leistungsdetails: eine Zeile je Eintrag mit
  Haken, Titel, Datum und Dauer; „Mehr“ klappt Auftraggeber, Fach und
  Beschreibung auf.

Auf allen drei Ebenen: **Fertig-Markierung** (die Ebene darüber zeigt „2 von 5
Aufgaben erledigt“), **Auftraggeber** und **Fach** (werden beim Anlegen von
oben übernommen und bleiben änderbar) sowie die **benötigte Arbeitszeit** als
umschaltbares Feld.

Die Listen der Aufträge und der Aufgaben haben **zwei Abschnitte**: zuoberst
**Offen**, darunter **Abgeschlossen**, jeder mit der Anzahl daneben. Der
Fertig-Haken schiebt den Eintrag sofort in den anderen Abschnitt; er bleibt
dabei ausgewählt, sodass rechts weiter dieselben Angaben stehen. Innerhalb
eines Abschnitts bleibt die gewohnte Reihenfolge erhalten.

**Enddatum** haben Auftrag und Aufgabe — eine neue Aufgabe übernimmt das Datum
ihres Auftrags, ein neuer Auftrag bekommt eines in zwei Wochen. Leistungsdetails
haben keines; dort wird die Zeit erfasst.

Im Kalender steht als Abgabe **nur das Enddatum des Auftrags**, und das auch
nur, solange er offen ist. Aufgaben erscheinen dort nicht: sie erben das Datum
ihres Auftrags und würden ihn bloss vervielfachen — ein Auftrag mit fünf
Aufgaben ergäbe sechs Abgaben am selben Tag. Wird der Auftrag abgehakt,
verschwindet seine Abgabe, denn sie steht nicht mehr an. Hausaufgaben bleiben
davon unberührt; die stehen mit „erledigt“ weiterhin an ihrem Termin.

Der Zeitrechner summiert fortlaufend nach oben: Aufgabe → Summe ihrer
Leistungsdetails, Auftrag → Summe seiner Aufgaben, Kopfzeile → Summe über alles,
als `3:15 h` und als Dezimalstunden `(3.25)`. Mit angegebener Soll-Zeit steht
dort „3:15 h von 6:00 h benötigt“. Die Dauer versteht `90` (Minuten), `1:30`,
`1,5h`, `2h`, `45m` und `1h30`.

### Hausaufgaben

Titel eintippen und mit `Enter` anlegen; dazu Fach, Abgabetermin, Fertig-Haken
und Notiz. Der Termin steht in Worten da („heute fällig“, „2 Tage überfällig“ —
überfällige rot), der Filter zeigt *Alle*, *Offen*, *Überfällig* oder
*Erledigt*. Jede Hausaufgabe mit Termin erscheint im Kalender.

Unter **Verweis** lässt sich eine Hausaufgabe mit einem Auftrag, einer Aufgabe
oder einem Leistungsdetail verknüpfen; „Öffnen“ springt dorthin.

### Prüfungen

**Neue Prüfung** anlegen, dann Fach, Thema, Datum, Beginn, Dauer, Raum und
Notiz ausfüllen. Die Prüfung steht damit sofort im Kalender; „Im Kalender
anzeigen“ springt in die passende Woche. **Prüfung exportieren…** gibt die
ausgewählte, **Alle exportieren…** alle Prüfungen als ICS-Datei aus.

### Kalender

Die Wochenansicht Montag bis Freitag führt alles zusammen:

* eigene Lektionen aus dem Stundenplan (von Hand gepflegt)
* Lektionen aus eingelesenen Stundenplan-Dateien
* Prüfungen und selbst erfasste Termine
* Hausaufgaben an ihrem Abgabetermin
* Enddaten offener Aufträge als Abgabe (Aufgaben stehen nicht im Kalender)

Alles außer Lektionen trägt eine kleine Marke („Prüfung“, „Hausaufgabe“,
„Abgabe“, „Termin“). Der heutige Tag ist hervorgehoben, Termine am Wochenende
stehen darunter. `◀` und `▶` blättern, „Heute“ springt zurück.

Ganztägige Einträge — Hausaufgaben, Abgaben und ganztägige Prüfungen oder
Termine — stehen in der Zeile „ganztägig“ über dem Zeitraster, jeder als eine
einzelne Zeile mit farbigem Streifen: Prüfung rot, Termin grün, Hausaufgabe
blau, Abgabe gelb. So passen auch an einem vollen Tag mehrere übereinander,
ohne das Raster zu verdrängen; Raum, Notiz und alles Weitere zeigt der Tooltip.

**Stundenplan bearbeiten**

Das **+** im Kopf einer Tagesspalte legt eine Lektion an diesem Wochentag an.
Im Fenster stehen Fach, Lehrkraft, Wochentag (oder ein einzelnes Datum),
Beginn, Dauer, Raum und Notiz. Eine so erfasste Lektion gilt jede Woche und
lässt sich jederzeit ändern.

Rechtsklick auf eine Lektion:

* **Bearbeiten…** — bei eigenen Lektionen direkt. Eine Lektion aus einer
  eingelesenen Datei wird dabei in den eigenen Stundenplan übernommen und die
  Vorlage aus der Datei an dieser Stelle ausgeblendet
* **Aus dem Stundenplan entfernen** — eigene Lektionen werden gelöscht,
  Lektionen aus Dateien nur ausgeblendet. **Ausgeblendete zurückholen** unten
  macht das wieder rückgängig

**Lehrkraft zuweisen**

Die Lehrkraft wird im Lektions-Fenster ausgewählt (die Liste kommt von der
Seite Lehrkräfte). Die Zuordnung gilt für **alle Lektionen dieses Fachs**, also
auch für die aus eingelesenen ICS-Dateien — einmal „Mathematik → Frau Meier“
genügt. In der Lektion steht die Lehrkraft dann neben dem Raum.

Davon lebt eine Bequemlichkeit: **legst du eine neue Aufgabe an, während gerade
eine Lektion läuft, wird deren Lehrkraft als Auftraggeber eingesetzt** — auch
wenn beim Auftrag jemand anderes steht. Die Statuszeile sagt, woher der Name
kommt. Läuft keine Lektion, gilt wie bisher der Auftraggeber des Auftrags.

**Einlesen**

* **Stundenplan-Datei einlesen…** — wiederkehrende Lektionen; die Datei wird
  ins Benutzerprofil kopiert, damit der Plan auch ohne das Original steht.
  Eine `.ics`-Datei ins Fenster ziehen macht dasselbe.
* **Termine importieren…** — einzelne Termine aus einer ICS-Datei werden als
  eigene Einträge übernommen (wiederkehrende werden übersprungen, die gehören
  als Stundenplan-Datei hinein).

Ausgewertet werden wöchentliche und tägliche Wiederholungen (`RRULE` mit
`BYDAY`, `INTERVAL`, `COUNT`, `UNTIL`), Ausnahmen (`EXDATE`), ganztägige
Termine und `DURATION`.

**Ausgeben**

* **Woche exportieren…** — alles aus der angezeigten Woche
* **Alles exportieren…** — ein Jahr zurück und ein Jahr voraus
* Rechtsklick auf einen Eintrag → **Diesen Termin exportieren…**

Die ICS-Dateien lesen Outlook, Google Kalender und Apple Kalender direkt ein.

### Lehrkräfte

Name eintippen und mit `Enter` anlegen; dazu Kürzel, Fach, E-Mail, Telefon,
Raum und Notiz. Lehrkräfte mit Adresse stehen auf der E-Mail-Seite in der
Auswahlliste neben dem Empfängerfeld — dort ausgewählt, werden sie als
Empfänger eingefügt. „E-Mail an diese Lehrkraft“ wechselt gleich zum Verfassen.

**Exportieren…** schreibt die Liste als CSV (Semikolon, mit BOM, öffnet direkt
in Excel), **Importieren…** liest sie wieder ein. Bekannte Adressen werden
aktualisiert statt doppelt angelegt. Spalten:
`Name;Kürzel;Fach;E-Mail;Telefon;Raum;Notiz`.

### E-Mail

Empfänger von Hand eintippen oder aus der Lehrkräfte-Auswahl einfügen,
`Cc / Bcc` klappt die weiteren Adressfelder auf. Anhänge über
**Hinzufügen…**, `Als HTML senden` schickt den Text als HTML. **Senden** oder
`Strg`+`Enter` verschickt die Nachricht.

### Einstellungen

Zuerst die **Konto-Art** wählen; danach zeigt die Seite nur die passenden Felder:

| Konto-Art | Was einzutragen ist |
|---|---|
| **SMTP-Server** | Host, Port, Verschlüsselung, Benutzername, Passwort |
| **Exchange-Server im Haus** | Host der Schule (z. B. `mail.schule.ch`), Port 587 mit STARTTLS, Benutzername als `DOMÄNE\benutzer` oder E-Mail-Adresse, Passwort |
| **Microsoft 365 / Exchange Online** | Nichts — nur einmal bei Microsoft anmelden |

Bei **SMTP-Server** und **Exchange-Server im Haus** genügt bei bekannten
Anbietern (Gmail, Outlook.com, Yahoo, iCloud, GMX, web.de, Bluewin, Sunrise,
...) die E-Mail-Adresse als Benutzername: Host, Port und Verschlüsselung
werden beim Verlassen des Feldes automatisch übernommen - auch wenn dort
schon ein anderer Server eingetragen war, denn solange nicht gespeichert
wird, geht nichts verloren. Bei allen anderen Anbietern - etwa dem eigenen
Schulserver - bleibt die manuelle Eingabe nötig.

**Verbindung testen** baut die Verbindung auf und meldet sich an, ohne etwas zu
verschicken.

#### Microsoft 365 einrichten

Konto-Art **Microsoft 365 / Exchange Online** wählen und **Mit Microsoft
anmelden** drücken — mehr ist es nicht. Der Browser öffnet die Anmeldung, danach
steht das Postfach fest; Server, Port und Passwort entfallen. Versendet wird
über **Microsoft Graph**, nicht über SMTP: Exchange Online hat die
SMTP-Anmeldung seit 2020 standardmässig abgeschaltet, und viele Schulen lassen
sie abgeschaltet. Die Nachricht landet wie gewohnt in „Gesendete Elemente“.
Es geht sowohl mit einem Schul- oder Geschäftskonto als auch mit einem privaten
Microsoft-Konto. Gesendet wird immer vom Postfach, mit dem man sich angemeldet
hat.

Bleibt die Anmeldung im Browser mit einer Fehlermeldung stehen, geht es mit
**Mit Code anmelden**: School Manager zeigt einen Code, legt ihn in die
Zwischenablage und öffnet die Seite von Microsoft, auf der er einzugeben ist.

Die Anmeldung wird mit der Windows-Datenschutz-API (DPAPI) verschlüsselt im
Benutzerprofil abgelegt und gilt auch nach einem Neustart.

Bei einem Schulkonto kann es sein, dass die Anmeldung mit „Zustimmung des
Administrators erforderlich“ abbricht — dann muss die Schul-IT die Berechtigung
`Mail.Send` für diese App einmalig freigeben. Ohne diese Freigabe bleibt nur
der Versand über Benutzername und Passwort.

Gmail und Outlook.com brauchen bei Zwei-Faktor-Anmeldung ein App-Passwort.

#### Programm aktualisieren

Bei jedem Start prüft School Manager im Hintergrund still auf eine neuere
Version; findet sich eine, erscheint oben im Fenster ein Hinweis darauf, der
direkt zu den Einstellungen führt.

Unter **Programm** steht die installierte Version. **Nach Updates suchen** fragt
die GitHub-Releases-Seite des Projekts ab; ist eine neuere Version vorhanden,
erscheint **Jetzt aktualisieren** — das lädt `SchoolManagerSetup.msi` mit
Fortschrittsanzeige herunter, beendet School Manager und installiert die neue
Version über die bestehende. Danach startet School Manager automatisch neu;
scheitert die Installation, startet die bisherige Version mit einer Meldung,
warum. Ist GitHub nicht erreichbar, sagt die Suche das, statt eine aktuelle
Version zu melden.

#### Daten exportieren und importieren

Unter **Daten**: **Konfiguration exportieren…** schreibt Konto-Art, Server und
Absender ohne Passwort als JSON-Datei (das Passwort ist mit DPAPI an dieses
Windows-Konto gebunden und liesse sich anderswo ohnehin nicht einlesen).
**Konfiguration importieren…** liest eine solche Datei wieder ein und übernimmt
sie sofort; das Passwort fehlt danach und muss neu eingegeben werden.

**Alle Daten exportieren…** packt den kompletten Ordner
`%APPDATA%\SchoolManager` — Aufträge, Hausaufgaben, Prüfungen, Lehrkräfte,
Stundenplan, To-Do, Notizen und Einstellungen — in eine ZIP-Datei, als
vollständige Sicherung. **Alle Daten importieren…** überschreibt damit den
Datenordner und startet School Manager anschliessend neu, damit alle Seiten
den eingelesenen Stand zeigen.

#### Zurücksetzen und deinstallieren

Unter **Zurücksetzen und Entfernen** stehen zwei Schaltflächen; beide fragen
vorher nach und lassen sich danach nicht rückgängig machen. Vorher lohnt sich
„Alle Daten exportieren…“.

* **Daten zurücksetzen…** löscht den Ordner `%APPDATA%\SchoolManager` und
  startet School Manager anschliessend neu. Das Programm bleibt installiert und
  beginnt wieder leer.
* **School Manager deinstallieren…** entfernt zusätzlich die Installation: die
  Rückfrage zählt auf, was verschwindet, danach ruft das Setup die
  Deinstallation auf (Programmordner, Startmenü- und Desktop-Verknüpfung,
  Eintrag in „Apps & Features“) und der Datenordner wird ebenfalls gelöscht.
  Läuft School Manager als einfach kopierte EXE, gibt es kein Setup — dann wird
  diese Datei selbst gelöscht. Welcher der beiden Fälle gilt, steht unter den
  Schaltflächen.

### Wo die Daten liegen

Alles im Benutzerprofil unter `%APPDATA%\SchoolManager` (Daten aus der Zeit, als
die App noch EmailSender hiess, werden beim ersten Start übernommen):

| Datei | Inhalt |
|---|---|
| `settings.json` | Konto und Server; das Passwort DPAPI-verschlüsselt |
| `m365-token.bin` | Anmeldung bei Microsoft 365, DPAPI-verschlüsselt |
| `auftraege.json` | Aufträge mit Aufgaben und Leistungsdetails |
| `hausaufgaben.json` | Hausaufgaben samt Verweisen |
| `termine.json` | Prüfungen und Termine |
| `lehrkraefte.json` | Lehrkräfte |
| `stundenplan.json` + `Stundenplan\*.ics` | Eingelesene Stundenplan-Dateien |
| `lektionen.json` | Eigene Lektionen des Stundenplans |
| `faecher.json` | Zuordnung Fach → Lehrkraft |
| `ausgeblendet.json` | Aus Dateien ausgeblendete Lektionen |
| `todos.json`, `notes.json` | To-Do-Liste und Notizen |
