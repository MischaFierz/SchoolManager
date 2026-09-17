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

## Einstellungen

Jede Einstellung lässt sich als Umgebungsvariable setzen. Statt eines Doppelpunkts steht dann ein doppelter Unterstrich.

| Einstellung | Wozu |
|---|---|
| `GitHub__Token` | Fein abgestuftes GitHub-Token für **beide** Repositories (`SchoolManager` und `SchoolManager-dev`). Rechte: *Contents* Lesen und Schreiben, *Actions* Lesen, *Metadata* Lesen. Ohne Token sieht der Server keine Dev-Versionen und kann nichts veröffentlichen. |
| `Admin__UserName`, `Admin__Password` | Nur für den allerersten Start |
| `Storage__Database` | Pfad der SQLite-Datei, Vorgabe `App_Data/schoolmanager.db` |
| `Server__BehindProxy` | `true` hinter Azure, nginx oder Caddy, damit die Bremse gegen Passwort-Raten die echte Adresse sieht |
| `GitHub__DevBranch` | Anfangswert des Entwicklungszweigs; danach im Panel unter Einstellungen änderbar |

Das Token gehört nie in `appsettings.json`, sondern in eine Umgebungsvariable oder in User Secrets:

```powershell
cd SchoolManager.Server
dotnet user-secrets set "GitHub:Token" "github_pat_…"
```

## Ins Internet stellen

Der Server braucht HTTPS und einen Ort, an dem die Datenbank-Datei bestehen bleibt. Ein Beispiel ist Azure App Service unter Linux:

1. Den Server als Linux-App veröffentlichen, etwa mit `dotnet publish SchoolManager.Server -c Release -o server-publish` und einem Deployment auf eine Web-App mit .NET 10.
2. Umgebungsvariablen setzen: `GitHub__Token`, `Server__BehindProxy=true`, `Storage__Database=/home/data/schoolmanager.db` und für den ersten Start `Admin__Password`.
3. In **beiden** GitHub-Repositories unter *Settings → Secrets and variables → Actions → Variables* die Variable `SERVER_URL` mit der Adresse des Servers anlegen, etwa `https://schoolmanager-admin.azurewebsites.net`.

Erst eine Fassung, die mit gesetztem `SERVER_URL` gebaut wurde, kennt den Server. Ohne diese Variable hat die Fassung keinen Entwicklermodus und sucht Updates nur bei GitHub.

## Niemand kommt mehr ins Panel

```powershell
dotnet SchoolManager.Server.dll --passwort-zuruecksetzen admin
```

Der Befehl setzt ein neues, einmaliges Passwort, gibt es aus, entsperrt das Konto und meldet es überall ab.

## Sicherheit

- **Passwörter:** Gespeichert wird nur ein Abdruck mit PBKDF2-SHA256 und 600 000 Durchläufen. Nach 5 Fehlversuchen ist das Konto 15 Minuten gesperrt. Zusätzlich sind je Internetadresse 10 Anmeldeversuche pro Minute erlaubt.
- **Anmeldungen:** Sie sind zufällige Tokens, gespeichert wird nur ihr SHA-256. Im Panel gilt eine Anmeldung 12 Stunden. In der App gilt sie 60 Tage und verlängert sich bei jeder Nutzung. Sperren, Löschen und neue Passwörter beenden sie sofort.
- **Rechte:** Niemand vergibt Rechte, die er selbst nicht hat. Nur Administratoren verwalten Administratoren, und der letzte aktive Administrator lässt sich weder sperren noch löschen.
- **Panel:** Es zeigt alle Daten als Text an, nie als HTML. Eine strenge Content-Security-Policy erlaubt nur Skripte vom Server selbst.
- **Dev-Versionen:** Der Server prüft, ob ein Konto eine Dev-Version beziehen darf, und gibt erst dann eine signierte Download-Adresse von GitHub heraus, die nur wenige Minuten gilt. Das GitHub-Token verlässt den Server nie.
