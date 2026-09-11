# Releases selber machen

Diese Anleitung beschreibt vollständig, wie aus dem Quellcode eine
veröffentlichte Version von School Manager wird - vom Tag bis zu dem Moment, in
dem die Update-Suche in der Anwendung sie anbietet.

---

## 1. Wie das Ganze funktioniert

Releases werden **nicht von Hand hochgeladen**. Es genügt, einen Versions-Tag
nach GitHub zu schieben; alles Weitere macht GitHub Actions:

```
git tag v1.2.0          →   GitHub Actions baut            →   Release auf GitHub
git push origin v1.2.0       (App + Installationspaket)         mit beiden Dateien
```

Der Ablauf steht in `.github/workflows/release.yml` und wird von jedem Tag der
Form `v*.*.*` ausgelöst. Der Arbeitsablauf

1. liest die Version aus dem Tag (`v1.2.0-dev` wird zu `1.2.0`, weil ein
   Installationspaket nur Zahlen verträgt),
2. installiert das WiX-Werkzeug,
3. baut `SchoolManager.exe` als eigenständige Datei mit `-p:Version=<Version>`,
4. baut daraus `SchoolManagerSetup.msi`,
5. legt das Release an und hängt beide Dateien an.

Ein Tag, der nicht auf `v*.*.*` passt (etwa `release-5` oder `v1.2`), löst gar
nichts aus. Dann passiert einfach nichts - ohne Fehlermeldung.

---

## 2. Die zwei Kanäle

| Tag | Was daraus wird | Wer es bekommt |
|---|---|---|
| `v1.2.0` | reguläres Release | jeder über „Nach Updates suchen" |
| `v1.2.0-dev` | Vorabversion (Prerelease) | nur der Entwicklermodus mit eingeschaltetem Patch-Kanal |

Der Unterschied entsteht allein durch das `-dev` im Tag: Der Arbeitsablauf setzt
damit das Kennzeichen `prerelease`, und die Update-Suche der Anwendung fragt für
normale Nutzer die GitHub-Auskunft `releases/latest` ab, die Vorabversionen von
sich aus überspringt.

**Faustregel:** Alles Ungetestete geht zuerst als `-dev` hinaus. Erst wenn es
sich bewährt hat, folgt derselbe Stand als reguläres Release.

---

## 3. Versionsnummern

Die Nummer besteht aus drei Teilen, `Haupt.Neben.Korrektur`:

- **Korrektur** (1.1.0 → 1.1.1): etwas repariert, sonst nichts Neues.
- **Neben** (1.1.0 → 1.2.0): neue Funktionen, alles Bisherige bleibt.
- **Haupt** (1.x → 2.0.0): grosse Umstellung.

Drei Regeln, die man kennen muss:

1. **Die Update-Suche bietet nur Neueres an.** Verglichen wird
   `Haupt.Neben.Korrektur`; ein Release mit gleicher oder kleinerer Nummer sieht
   niemand. Eine Version „nachbessern" geht deshalb nicht - es braucht immer eine
   neue Nummer.
2. **Das Installationspaket lehnt Rückschritte ab.** Eine kleinere Version lässt
   sich nicht über eine grössere installieren; sie muss vorher deinstalliert
   werden. Genau das macht der Rückweg aus dem Entwicklermodus automatisch.
3. **Die Nummer im Tag gewinnt.** Die Dateien tragen die Version aus dem Tag,
   nicht die aus der Projektdatei. Trotzdem sollte beides übereinstimmen, sonst
   zeigt eine selbst gebaute Fassung eine andere Nummer als die veröffentlichte.

---

## 4. Ein reguläres Release veröffentlichen

Voraussetzung: Der Stand auf `master` ist der, der hinausgehen soll, und er
lässt sich bauen (`dotnet build SchoolManager.sln -c Release`).

**Schritt 1 - Version in der Projektdatei setzen.**
In `SchoolManager.App/SchoolManager.App.csproj`:

```xml
<Version>1.2.0</Version>
```

**Schritt 2 - Änderung festhalten.**

```bash
git add -A
git commit -m "fix: version 1.2.0"
git push origin master
```

**Schritt 3 - Tag setzen und schieben.**

```bash
git tag v1.2.0
git push origin v1.2.0
```

**Schritt 4 - Zusehen.**
Der Bau dauert etwa drei Minuten (das eigenständige Programm ist rund 68 MB):

<https://github.com/MischaFierz/SchoolManager/actions>

**Schritt 5 - Nachsehen, ob alles da ist.**
Unter <https://github.com/MischaFierz/SchoolManager/releases> muss das neue
Release stehen, als *Latest* markiert, mit **beiden** Dateien:
`SchoolManager.exe` und `SchoolManagerSetup.msi`.

Fehlt `SchoolManagerSetup.msi`, findet die Update-Suche das Release nicht - sie
sucht genau diesen Dateinamen.

---

## 5. Eine Vorabversion (Dev-Patch) veröffentlichen

Genau dasselbe, nur mit `-dev` am Tag:

```bash
# Version in der Projektdatei auf 1.2.1 setzen, dann:
git add -A
git commit -m "fix: was auch immer repariert wurde"
git push origin master

git tag v1.2.1-dev
git push origin v1.2.1-dev
```

Die Nummer muss **höher** sein als die zuletzt installierte, sonst bietet die
Update-Suche sie nicht an. Wer 1.2.0 installiert hat, bekommt `v1.2.1-dev`;
ein zweites `v1.2.0-dev` sähe er nie.

**In der Anwendung ankommen:**

1. Unten links siebenmal auf die Versionsnummer klicken - das schaltet den
   Entwicklermodus frei.
2. Einstellungen → Entwickler → „Dev-Patches statt Releases beziehen" ankreuzen.
3. Einstellungen → Programm → „Nach Updates suchen".

Beim Einschalten des Entwicklermodus legt die Anwendung von sich aus eine
Sicherung aller Daten an (`%LOCALAPPDATA%\SchoolManager\Sicherungen`).
„Entwicklermodus verlassen" bietet den ganzen Rückweg an: letzte öffentliche
Version installieren und die Sicherung einspielen.

---

## 6. Ein Release wieder löschen

Zwei Dinge müssen weg - das Release **und** der Tag. Nur den Tag zu löschen
genügt nicht; das Release bliebe als verwaister Eintrag stehen.

Am einfachsten im Browser: Release öffnen → *Delete*. Danach der Tag:

```bash
git push origin --delete v1.2.1-dev
git tag -d v1.2.1-dev
```

Mit dem `gh`-Werkzeug (falls einmal installiert) in einem Rutsch:

```bash
gh release delete v1.2.1-dev --cleanup-tag
```

Achtung: Eine gelöschte Version lässt sich nur wiederherstellen, indem man sie
neu baut. Die Commits bleiben davon unberührt - verloren geht nur das Release.

---

## 7. Einen Fehler beheben, ohne alles Neue mitzuveröffentlichen

Der häufige Fall: Auf `master` liegen schon halbfertige Neuerungen, aber die
veröffentlichte Version hat einen Fehler, der sofort weg muss.

Dann wird der Fehler **auf dem alten Stand** behoben:

```bash
# Vom Tag der veröffentlichten Version abzweigen
git checkout -b hotfix/1.0.2 v1.0.1

# ... Fehler beheben, Version in der Projektdatei auf 1.0.2 setzen ...

git add -A
git commit -m "fix: das Problem kurz benannt"
git tag v1.0.2
git push origin v1.0.2          # nur den Tag - der Zweig muss nicht hinauf
```

Damit enthält das Release ausschliesslich den alten Stand plus die Korrektur.
Danach muss dieselbe Korrektur noch auf `master`, sonst ist sie beim nächsten
regulären Release wieder verschwunden:

```bash
git checkout master
git checkout hotfix/1.0.2 -- Pfad/zur/geaenderten/Datei.cs   # oder: git cherry-pick <commit>
git commit -m "fix: dasselbe wie in 1.0.2"
git push origin master

git branch -D hotfix/1.0.2     # der Tag hält den Commit fest, der Zweig darf weg
```

---

## 8. Ohne Release bauen (zum Ausprobieren)

Für einen Stand, der nicht veröffentlicht werden soll:

```bat
publish.cmd           :: ergibt publish\SchoolManager.exe
build-installer.cmd   :: ergibt zusätzlich publish\SchoolManagerSetup.msi
```

`build-installer.cmd` braucht einmalig das WiX-Werkzeug:

```bash
dotnet tool install --global wix
```

Diese Dateien nehmen die Version aus der Projektdatei, nicht aus einem Tag.

---

## 9. Wie die Anwendung Updates findet

Nachzulesen in `SchoolManager.App/Update/UpdateService.cs`:

- **Normal:** fragt `releases/latest` ab. Vorabversionen sind darin nicht
  enthalten.
- **Im Entwicklermodus mit Patch-Kanal:** holt die letzten 50 Releases, behält
  die mit `-dev` im Tag und nimmt davon die höchste Version.
- In beiden Fällen muss das Release die Datei `SchoolManagerSetup.msi`
  enthalten und eine höhere Nummer tragen als die laufende Fassung.
- Aus dem Tag wird alles ab dem Bindestrich abgeschnitten: `v1.2.0-dev` gilt als
  Version `1.2.0`.

Beim Aktualisieren lädt die Anwendung das Installationspaket in den
Temp-Ordner, startet `msiexec`, beendet sich und startet danach neu.

---

## 10. Stolpersteine

**Der Tag ist schon vergeben.** Ein Tag lässt sich nicht einfach verschieben.
Entweder eine neue Nummer nehmen (fast immer richtig) oder Release und Tag
zuerst löschen, wie in Abschnitt 6.

**Nichts passiert nach dem Schieben.** Fast immer ein Tag, der nicht auf
`v*.*.*` passt. Prüfen: `git tag` zeigt die lokalen, `git ls-remote --tags
origin` die auf GitHub.

**Der Bau ist rot.** Die Ausgabe steht unter *Actions*. Zuerst lokal
nachstellen:

```bash
dotnet build SchoolManager.sln -c Release
```

Nach einem gescheiterten Bau gibt es kein Release, aber der Tag bleibt stehen -
also löschen, reparieren und mit demselben Tag noch einmal schieben (oder
gleich die nächste Nummer nehmen).

**Ein Release zeigt die falsche Nummer.** Dann wichen Tag und Projektdatei
voneinander ab. Die Dateien tragen immer die Nummer aus dem Tag.

**Niemand bekommt das Update.** Nummer nicht erhöht, `-dev` vergessen oder
zu viel, oder die MSI-Datei fehlt im Release.

---

## 11. Alles auf einen Blick

```bash
# Reguläres Release
#   (vorher: <Version> in SchoolManager.App/SchoolManager.App.csproj setzen)
git add -A && git commit -m "fix: version 1.2.0" && git push origin master
git tag v1.2.0 && git push origin v1.2.0

# Vorabversion
git tag v1.2.1-dev && git push origin v1.2.1-dev

# Löschen
git push origin --delete v1.2.1-dev && git tag -d v1.2.1-dev
#   (das Release selbst im Browser löschen)

# Stand prüfen
git tag                                  # lokale Tags
git ls-remote --tags origin              # Tags auf GitHub
```

Nachsehen:
[Actions](https://github.com/MischaFierz/SchoolManager/actions) ·
[Releases](https://github.com/MischaFierz/SchoolManager/releases)
