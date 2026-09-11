# School Manager

Windows-Desktop-App für die Schule: Aufträge mit Aufgaben und Leistungsdetails
samt Zeitrechner, Hausaufgaben, Prüfungen, ein Kalender, der alles zusammen
zeigt und als ICS aus- und einlesen kann, Lehrkräfte mit Empfängerauswahl,
E-Mail-Versand (auch über Exchange), To-Do-Liste und Notizen. Dazu ein
Kommandozeilen-Programm für den Versand aus Skripten.

**[⬇ Neueste Version herunterladen](https://github.com/MischaFierz/SchoolManager/releases/latest)**

## Installieren

`SchoolManagerSetup.msi` aus dem Release oben herunterladen und doppelklicken —
oder selbst bauen (siehe unten), dann liegt es unter
`publish\SchoolManagerSetup.msi`. Das Paket installiert ohne
Administratorrechte nach `%LOCALAPPDATA%\Programs\School Manager`, legt eine
Verknüpfung im Startmenü und auf dem Desktop an und erscheint in „Apps &
Features“ zum Deinstallieren. Die .NET-Laufzeit ist enthalten, es muss nichts
weiter installiert werden.

Wer die App nur kopieren möchte: `SchoolManager.exe` ist eine einzige Datei und
läuft von jedem Ort, auch von einem Stick.

Der Ordner `publish\` entsteht erst beim Bauen und liegt bewusst nicht im
Repository: die beiden Dateien sind zusammen rund 125 MB und wären nach jedem
Bauvorgang neu.

## Selbst bauen

```powershell
.\publish.cmd           # nur die EXE  -> publish\SchoolManager.exe
.\build-installer.cmd   # EXE + Setup  -> publish\SchoolManagerSetup.msi
```

Für das Setup wird einmalig das WiX-Werkzeug gebraucht:

```powershell
dotnet tool install --global wix --version 5.0.2
```

(Version 5 absichtlich: ab Version 6 verlangt WiX das Akzeptieren einer
Gebühren-Lizenz.)

Zum Entwickeln genügt `dotnet run --project SchoolManager.App`.
Die Projektmappe heisst `SchoolManager.sln`.

### Neue Version veröffentlichen

Die Versionsnummer steht einzig in `SchoolManager.App/SchoolManager.App.csproj`
(`<Version>`) — dort erhöhen, committen, dann als Tag pushen:

```powershell
git tag v1.1.0
git push origin v1.1.0
```

Der Workflow `.github/workflows/release.yml` baut daraufhin automatisch
`SchoolManager.exe` und `SchoolManagerSetup.msi` mit der Version aus dem Tag
und veröffentlicht beides als GitHub-Release. Die eingebaute Update-Suche
(Einstellungen → Programm) findet dieses Release automatisch, sobald es
veröffentlicht ist.

#### Zwei Kanäle: Release und Dev-Patch

Am Tag hängt, wer den Stand zu sehen bekommt:

| Tag | Was daraus wird | Wer bekommt es |
|---|---|---|
| `v1.2.0` | normales Release | alle |
| `v1.2.0-dev` | Vorabversion (prerelease) | nur der Entwicklermodus |

Der Workflow setzt das selbst: Enthält der Tag `-dev`, wird die
Veröffentlichung als Vorabversion markiert. Die Update-Suche normaler Nutzer
fragt `releases/latest` ab, und das überspringt Vorabversionen grundsätzlich —
Dev-Patches werden ihnen also nie angeboten. Der numerische Teil der Version
zählt in beiden Kanälen gemeinsam weiter, damit ein Dev-Patch immer neuer ist
als das Release davor.

Die Tags `v1.1.0-dev` bis `v1.7.0-dev` sind der Stand aus der Entwicklung vor
dem ersten öffentlichen Release; sie liegen als Vorabversionen im Archiv.

## Projekte

| Projekt | Was es ist |
|---|---|
| `SchoolManager.App` | Die Desktop-App (WPF). Ergebnis: `SchoolManager.exe` |
| `SchoolManager.Cli` | Konsolen-Programm für den E-Mail-Versand aus Skripten |
| `SchoolManager.Core` | Gemeinsamer Kern: `SmtpSettings`, `OutgoingEmail`, `EmailService`, `EmailProviderPresets`, `GraphMailService` |
| `installer/SchoolManager.wxs` | Beschreibung des Installationspakets (WiX) |
| `tools/make-icon.ps1` | Erzeugt `SchoolManager.App/app.ico` neu (Doktorhut im Akzentgrau) |

Innerhalb von `SchoolManager.App`:

| Datei | Zweck |
|---|---|
| `MainWindow.xaml` | Rahmen: Seitennavigation links, aktive Seite rechts, Statusleiste unten |
| `UpdateToast.xaml` | Kurzer Hinweis unten rechts, wenn beim Start ein Update gefunden wurde |
| `Theme.xaml` | Farben und Vorlagen aller Steuerelemente (Graustufen, weisse Schrift) |
| `Pages/OrdersPage` | Aufträge mit der Liste ihrer Aufgaben |
| `Pages/TasksPage` | Alle Aufgaben; je Aufgabe die Liste der Leistungsdetails |
| `Pages/HomeworkPage` | Hausaufgaben mit Verweis auf Auftrag/Aufgabe/Leistungsdetail |
| `Pages/ExamsPage` | Prüfungen erfassen und ausgeben |
| `Pages/CalendarPage` | Wochenansicht über alle Quellen, Import und Export |
| `Pages/TeachersPage` | Lehrkräfte samt CSV-Export und -Import |
| `Pages/MailPage` | E-Mail verfassen und senden |
| `Pages/TodoPage`, `Pages/NotesPage` | To-Do-Liste und Notizen |
| `Pages/SettingsPage` | Konto-Art, Server, Anmeldung, Absender, Programm-Update, Datenexport |
| `Data/WorkOrder`, `WorkTask`, `WorkEntry` | Die drei Ebenen mit Fertig-Markierung, Soll-/Ist-Zeit und Enddatum |
| `Data/WorkStore`, `HomeworkStore`, `EventStore`, `TeacherStore`, `LessonPlanStore` | Die gemeinsam genutzten Datenspeicher |
| `Data/TimetableEntry` | Eine selbst erfasste Lektion, wöchentlich oder einmalig |
| `Dialogs/LessonDialog` | Fenster zum Anlegen und Bearbeiten einer Lektion |
| `Data/CalendarFeed` | Führt alle Quellen zusammen und findet die gerade laufende Lektion |
| `Data/IcsParser`, `IcsWriter`, `TimetableStore` | ICS lesen und schreiben, Quellen verwalten |
| `Data/TimeText` | Der Zeitrechner: liest `90`, `1:30`, `1,5h` und rechnet Summen |
| `DoneGrouping` | Teilt die Listen der Aufträge und Aufgaben in „Offen“ und „Abgeschlossen“ |
| `DevMode` | Der Entwicklermodus: Dev-Patches und die Konto-Art Microsoft 365 |
| `Microsoft365TokenSource` | Anmeldung bei Microsoft 365 (OAuth2) für den SMTP-Versand |
| `Update/UpdateService` | Prüft GitHub Releases auf eine neuere Version und lädt das Setup herunter |
| `Update/UninstallService` | Daten zurücksetzen und School Manager wieder vom Rechner entfernen |
| `Data/DataExportService` | Konfiguration bzw. alle Daten als JSON/ZIP exportieren |
| `Data/DataImportService` | Konfiguration bzw. alle Daten aus JSON/ZIP wieder einlesen |

## Die App

Links liegt die Seitennavigation: **Aufträge**, **Aufgaben**, **Hausaufgaben**,
**Prüfungen**, **Kalender**, **Lehrkräfte**, **E-Mail**, **To-Do**, **Notizen**
und, unten abgesetzt, **Einstellungen**. `Strg`+`1` bis `Strg`+`9` schalten
direkt um, `Strg`+`0` zu den Einstellungen. Unten quer läuft eine gemeinsame
Statuszeile: grau für Hinweise, grün für Erfolg, rot für Fehler.

Oben steht ein roter Streifen: **der E-Mail-Versand ist in dieser Version noch
nicht verfügbar und wird mit einem späteren Update nachgereicht.** Die Seite
E-Mail und die Postausgangs-Einstellungen lassen sich zwar öffnen, verlassen
sollte man sich aber auf nichts davon. Daneben führen **Einstellungen öffnen**
dorthin und **✕** blendet den Hinweis bis zum nächsten Start aus.

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
| **Microsoft 365 / Exchange Online** | E-Mail-Adresse des Postfachs, Anwendungs-ID, optional Verzeichnis-ID — Server und Port sind vorgegeben |

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
Microsoft-Konto.

Die Anmeldung wird mit der Windows-Datenschutz-API (DPAPI) verschlüsselt im
Benutzerprofil abgelegt und gilt auch nach einem Neustart.

**Damit das ein einzelner Knopf sein kann**, muss in School Manager eine
Anwendungs-ID stecken — ohne registrierte App gibt es bei Microsoft keine
Anmeldung. Sie steht als `BuiltInClientId` in
`SchoolManager.Core/SmtpSettings.cs`. Solange dort nichts eingetragen ist,
zeigt die Seite die Felder **Anwendungs-ID** und **Verzeichnis-ID** an und
jeder muss seine eigene Registrierung eintragen.

Die Registrierung wird einmalig angelegt (Azure-Portal, kostenlos, kein
Abonnement nötig):

1. [portal.azure.com](https://portal.azure.com) → *App-Registrierungen* →
   **Neue Registrierung**
2. Name z. B. `School Manager`; unter *Unterstützte Kontotypen* **Konten in
   einem beliebigen Organisationsverzeichnis und persönliche
   Microsoft-Konten** wählen
3. *Authentifizierung* → **Plattform hinzufügen** → **Mobile Geräte und
   Desktopanwendungen** → `http://localhost` ankreuzen
4. *API-Berechtigungen* → **Berechtigung hinzufügen** → *Microsoft Graph* →
   **Delegierte Berechtigungen** → **Mail.Send** und **User.Read**
5. Die **Anwendungs-ID (Client)** von der Übersichtsseite kopieren und als
   `BuiltInClientId` eintragen

`Mail.Send` ist fürs Verschicken da, `User.Read` nur fürs Nachsehen: „Verbindung
testen“ und die Anzeige, als wer man angemeldet ist, fragen bei Graph `/me` ab,
und das lässt Microsoft ohne `User.Read` nicht zu. Fehlt sie, schlägt der
Verbindungstest mit „keine Berechtigung“ fehl, obwohl das Senden selbst ginge.

Mit der Azure-Befehlszeile geht dasselbe in einem Schritt (`az login
--allow-no-subscriptions` vorausgesetzt):

```bash
az ad app create --display-name "School Manager" \
  --sign-in-audience AzureADandPersonalMicrosoftAccount \
  --public-client-redirect-uris http://localhost \
  --required-resource-accesses '[{"resourceAppId":"00000003-0000-0000-c000-000000000000","resourceAccess":[{"id":"e383f46e-2787-4529-855e-0e479a3ffac0","type":"Scope"},{"id":"e1fe6dd8-ba31-4d61-89e7-88639da4683d","type":"Scope"}]}]' \
  --query appId -o tsv
```

Ein privates Microsoft-Konto braucht dafür einmal ein Verzeichnis: Wer noch nie
im Azure-Portal war, hat keines, und die Befehlszeile findet dann nichts zum
Anmelden. Einmal [portal.azure.com](https://portal.azure.com) öffnen legt es an.

Bei einem Schulkonto kann es sein, dass die Anmeldung mit „Zustimmung des
Administrators erforderlich“ abbricht — dann muss die Schul-IT die Berechtigung
`Mail.Send` für diese App einmalig freigeben. Ohne diese Freigabe bleibt nur
der Versand über Benutzername und Passwort.

Gmail und Outlook.com brauchen bei Zwei-Faktor-Anmeldung ein App-Passwort.

#### Programm aktualisieren

Bei jedem Start prüft School Manager im Hintergrund still auf eine neuere
Version; findet sich eine, erscheint unten rechts kurz ein Hinweis darauf (er
verschwindet nach 5 Sekunden von selbst oder auf Klick auf das ✕). Ein Klick
auf den Hinweis selbst springt direkt zu den Einstellungen.

Unter **Programm** steht die installierte Version. **Nach Updates suchen** fragt
die GitHub-Releases-Seite des Projekts ab; ist eine neuere Version vorhanden,
erscheint **Jetzt aktualisieren** — das lädt `SchoolManagerSetup.msi` herunter,
beendet School Manager und startet das Setup, das die bestehende Installation
per Major-Upgrade ersetzt. Nach Abschluss der Installation startet School
Manager automatisch neu. Ohne Internetverbindung oder ohne veröffentlichtes
Release meldet die Suche, dass bereits die aktuellste Version läuft.

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

#### Entwicklermodus

Manches ist noch nicht für alle gedacht. Es liegt deshalb hinter einem
Entwicklermodus, der sich freischalten lässt, indem man unten links in der
Seitenleiste **siebenmal auf die Versionsnummer klickt**. Danach steht dort
„Version 1.0.0 · Dev“, und in den Einstellungen erscheint der Abschnitt
**Entwickler**.

Er bringt zweierlei:

* **Dev-Patches statt Releases beziehen** — die Update-Suche nimmt dann die
  Vorabversionen mit `-dev` im Namen. Diese Stände sind ungetestet; vorher
  lohnt sich „Alle Daten exportieren…“.
* Die Konto-Art **Microsoft 365 / Exchange Online** wird sichtbar, solange keine
  Anwendungs-ID eingebaut ist - ohne sie führt die Anmeldung ins Leere (siehe
  „Microsoft 365 einrichten“). Steckt eine ID im Programm, steht die Konto-Art
  ohnehin allen offen und der Entwicklermodus ändert daran nichts mehr.

**Entwicklermodus verlassen** schaltet beides wieder ab. Der Zustand steht in
`%APPDATA%\SchoolManager\entwickler.json`.

Das ist eine Sichtbarkeits-, keine Sicherheitsfrage: Was in der Anwendung
steckt, lässt sich ohnehin auslesen. Es geht darum, Unfertiges niemandem
versehentlich vor die Nase zu setzen.

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

Beides erledigt ein kleines Aufräum-Skript im Temp-Ordner statt der App selbst:
ein laufendes Programm kann sich weder löschen noch verhindern, dass die Seiten
beim Schliessen ihre Eingaben nochmals speichern. Das Skript wartet, bis School
Manager beendet ist, räumt auf und löscht sich zuletzt selbst.

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

## Das Konsolen-Programm

`SchoolManager.Cli` verschickt E-Mails aus Skripten; die Konfiguration steht in
`SchoolManager.Cli/appsettings.json`:

```json
{
  "Smtp": {
    "Host": "smtp.gmail.com",
    "Port": 587,
    "Security": "StartTls",
    "UserName": "dein.name@gmail.com",
    "Password": "",
    "FromAddress": "dein.name@gmail.com",
    "FromDisplayName": "Dein Name"
  }
}
```

Das Passwort gehört nicht in diese Datei, sondern in User Secrets oder eine
Umgebungsvariable — doppelter Unterstrich statt Doppelpunkt:

```powershell
cd SchoolManager.Cli
dotnet user-secrets set "Smtp:Password" "dein-passwort"
# oder
$env:Smtp__Password = "dein-passwort"
```

```powershell
dotnet run -- --to max@example.com --subject "Hallo" --body "Kurzer Test"
dotnet run -- --help
```

Bei Erfolg ist der Rückgabewert `0`, bei einem Fehler `1`. Die Konsolen-Variante
nutzt Benutzername und Passwort; für Microsoft 365 mit OAuth2 ist die
Desktop-App gedacht.

## Als Bibliothek nutzen

```csharp
using SchoolManager.Core;

var settings = new SmtpSettings
{
    Host = "smtp.example.com",
    Port = 587,
    Security = SmtpSecurity.StartTls,
    UserName = "user@example.com",
    Password = "...",
    FromAddress = "user@example.com"
};

var mail = new OutgoingEmail { Subject = "Hallo", Body = "Nachricht" };
mail.To.Add("empfaenger@example.com");

await new EmailService(settings).SendAsync(mail);
```

`TestConnectionAsync()` prüft Server und Anmeldung, ohne eine Mail zu senden.
Für Microsoft 365 nimmt `EmailService` zusätzlich eine `IAccessTokenSource`.
