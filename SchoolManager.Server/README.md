# School-Manager-Server

Der Server hinter dem Entwicklermodus und das Admin-Panel im Browser.

- **Anmeldung für den Entwicklermodus:** Nur Konten, die hier eingetragen sind, kommen hinein.
- **Meldungen:** Streifen oben im Fenster von School Manager, auf der Startseite oder auf der Anmeldeseite, rot für Fehler oder grau für Hinweise.
- **Update-Infos:** ein kurzer Text zu jeder Version.
- **Freigaben:** welche Dev-Version welches Konto oder welche Gruppe bekommt.
- **Releases:** Dev-Versionen und öffentliche Releases auslösen und danach aufräumen.
- **Benutzer, Stufen, Rechte, Gruppen und Verlauf.**

Unter der Adresse des Servers liegt eine Startseite mit den Funktionen und dem Download der neuesten Version, lokal also <http://localhost:5080>. Oben rechts führt „Anmelden“ ins Admin-Panel unter `/admin/`.

## Lokal starten

```powershell
dotnet run --project SchoolManager.Server
```

Eine Debug-Fassung von School Manager spricht von selbst mit `http://localhost:5080`.

Beim ersten Start ist die Datenbank leer. Der Server legt dann einen Administrator an:

- Name aus `Admin:UserName`, sonst `admin`.
- Passwort aus `Admin:Password`. Fehlt es, erzeugt der Server eines und schreibt es einmal ins Log. Dieses Passwort muss bei der ersten Anmeldung ersetzt werden.

Zum dauerhaften Betrieb auf dem eigenen Rechner läuft besser eine veröffentlichte
Kopie unter `%LOCALAPPDATA%\SchoolManager-Server`, gestartet über die
Desktop-Verknüpfung „School Manager Server (lokal)“. So sperrt der laufende Server
beim Bauen keine Dateien im Projekt. Eine neue Fassung dorthin bringen (Server
vorher beenden):

```powershell
dotnet publish SchoolManager.Server -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o "$env:LOCALAPPDATA\SchoolManager-Server"
```

## Tests

```powershell
dotnet test SchoolManager.Server.Tests
```

Die Tests starten den Server mit einer leeren Datenbank im Temp-Ordner und einem
nachgestellten GitHub. Sie brauchen weder Netz noch Token und laufen vor jedem
Hochladen auch im Workflow.

## Stile

Die Stile liegen als SCSS in `Styles/`: `site.scss` für die Startseite,
`admin/styles.scss` für Panel und Anmeldeseite, `_tokens.scss` mit Farben,
Bildschirmbreiten und gemeinsamen Mixins. Beim Bauen übersetzt das Paket
AspNetCore.SassCompiler sie nach `wwwroot/` (Debug lesbar, Release verkleinert).
Die CSS-Dateien dort sind erzeugt und nicht im Repository.

## Einstellungen

Jede Einstellung lässt sich als Umgebungsvariable setzen. Statt eines Doppelpunkts steht dann ein doppelter Unterstrich.

| Einstellung | Wozu |
|---|---|
| `GitHub__Token` | Fein abgestuftes GitHub-Token für **beide** Repositories (`SchoolManager` und `SchoolManager-dev`). Rechte: *Contents* Lesen und Schreiben, *Actions* Lesen, *Metadata* Lesen. Ohne Token sieht der Server keine Dev-Versionen und kann nichts veröffentlichen. |
| `Admin__UserName`, `Admin__Password` | Nur für den allerersten Start |
| `Storage__Database` | Arbeitsdatei der SQLite-Datenbank, Vorgabe `App_Data/schoolmanager.db` |
| `Storage__Backup` | Laufend nachgeführte Kopie, etwa auf einem dauerhaften Laufwerk. Fehlt die Arbeitsdatei beim Start, holt der Server diese Kopie zurück. Leer: keine Kopie |
| `Storage__Snapshots` | Ordner für die täglichen Sicherungen (14 Tage), Vorgabe `Sicherungen` neben der Kopie bzw. der Datenbank |
| `Server__BehindProxy` | `true` hinter Azure, nginx oder Caddy, damit die Bremse gegen Passwort-Raten die echte Adresse sieht |
| `GitHub__DevBranch` | Anfangswert des Entwicklungszweigs; danach im Panel unter Einstellungen änderbar |

Das Token gehört nie in `appsettings.json`, sondern in eine Umgebungsvariable oder in User Secrets:

```powershell
cd SchoolManager.Server
dotnet user-secrets set "GitHub:Token" "github_pat_…"
```

## Ins Internet stellen

Der Server läuft auf **Azure App Service** (Linux, Gratis-Plan F1): Web-App
`school-manager-ch` in der Ressourcengruppe `rg-schoolmanager`.

**Hochladen** übernimmt der Workflow `.github/workflows/deploy-server.yml` im
privaten Repository: bei jedem Push auf einen `entwicklung-*`-Zweig, der den
Server betrifft, oder von Hand unter *Actions → Server hochladen → Run workflow*.
Er testet, veröffentlicht vorkompiliert, lädt hoch, setzt erst danach den
Startbefehl und prüft, ob der Server antwortet. Bei Azure meldet er sich ohne
gespeichertes Passwort an (OIDC): Die App-Registrierung „SchoolManager Server
Deploy“ vertraut nur Läufen aus `MischaFierz/SchoolManager-dev` in der
GitHub-Umgebung `azure` und darf nur diese Web-App ändern.

**Einstellungen der Web-App:**

- `GitHub__Token`
- `Server__BehindProxy=true`
- `Storage__Database=/tmp/schoolmanager/schoolmanager.db`: Arbeitsdatei auf der lokalen Platte, denn SQLite verträgt das Netzlaufwerk unter `/home` schlecht.
- `Storage__Backup=/home/data/schoolmanager.db` und `Storage__Snapshots=/home/data/Sicherungen`: dauerhaft.
- `Admin__Password` nur für den allerersten Start.

**Gratis-Plan F1:** 60 CPU-Minuten am Tag und kein „Always On“. Der erste Aufruf
nach einer Pause dauert deshalb einige Sekunden. Ist das Kontingent aufgebraucht,
sperrt Azure die App bis Mitternacht (UTC); der Workflow überspringt das
Hochladen dann mit einem Hinweis.

In **beiden** GitHub-Repositories steht unter *Settings → Secrets and variables →
Actions → Variables* die Variable `SERVER_URL` mit der Adresse des Servers.

> Mit Git Bash unter Windows vor `az`-Befehlen `MSYS_NO_PATHCONV=1` setzen, sonst wird ein Pfad wie `/home/data/…` zu `C:/Program Files/Git/home/data/…`.

Erst eine Fassung, die mit gesetztem `SERVER_URL` gebaut wurde, kennt den Server. Ohne diese Variable hat die Fassung keinen Entwicklermodus und sucht Updates nur bei GitHub.

## Niemand kommt mehr ins Panel

```powershell
dotnet SchoolManager.Server.dll --passwort-zuruecksetzen admin
```

Der Befehl setzt ein neues, einmaliges Passwort, gibt es aus, entsperrt das Konto und meldet es überall ab.

## Sicherheit

- **Passwörter:** Gespeichert wird nur ein Abdruck mit PBKDF2-SHA256 und 600 000 Durchläufen. Nach 5 Fehlversuchen ist das Konto 15 Minuten gesperrt. Zusätzlich sind je Internetadresse 10 Anmeldeversuche pro Minute erlaubt.
- **Anmeldungen:** Sie sind zufällige Tokens, gespeichert wird nur ihr SHA-256. Im Panel gilt eine Anmeldung 12 Stunden. In der App gilt sie 1 Jahr und verlängert sich bei jeder Nutzung. Sperren, Löschen und neue Passwörter beenden sie sofort.
- **Rechte:** Niemand vergibt Rechte, die er selbst nicht hat. Nur Administratoren verwalten Administratoren, und der letzte aktive Administrator lässt sich weder sperren noch löschen.
- **Panel:** Es zeigt alle Daten als Text an, nie als HTML. Eine strenge Content-Security-Policy erlaubt nur Skripte vom Server selbst.
- **Dev-Versionen:** Nur School Manager ab 1.2.0 bekommt sie. Der Server prüft, ob ein Konto eine Dev-Version beziehen darf, und gibt erst dann eine signierte Download-Adresse von GitHub heraus, die nur wenige Minuten gilt. Das GitHub-Token verlässt den Server nie.
- **Sicherungen:** Die Datenbank wird nach jeder Änderung spätestens nach einer Minute und beim Beenden in die Kopie geschrieben, dazu täglich eine Sicherung für 14 Tage. Abgelaufene Anmeldungen räumt der Server alle sechs Stunden weg.
- **App:** Die Anmeldung im Entwicklermodus liegt mit DPAPI verschlüsselt im Benutzerprofil - wie das Mail-Passwort.
