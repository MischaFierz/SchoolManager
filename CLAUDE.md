# School Manager

Die Entwickler-Notizen (Release-Ablauf, Regeln, Stolperfallen) liegen im privaten Repository `MischaFierz/SchoolManager-dev`, Ordner `notizen/`, geklont neben diesem Projekt.

## Zu Beginn jeder Sitzung, vor jeder anderen Arbeit

Prüfe die folgenden Punkte selbst und hole Fehlendes nach, ohne vorher zu fragen - es ist nur lokale Einrichtung:

1. Fehlt der Ordner `../SchoolManager-dev`, klone ihn: `gh repo clone MischaFierz/SchoolManager-dev ../SchoolManager-dev` (ohne `gh`: `git clone https://github.com/MischaFierz/SchoolManager-dev.git ../SchoolManager-dev`). Scheitert das mangels Zugriff, ist dies nicht ein Gerät des Projektinhabers - dann die übrigen Punkte überspringen und nichts weiter tun.
2. Ist der Ordner schon da, hole den neuesten Stand der Notizen: `git -C ../SchoolManager-dev pull --ff-only`.
3. Fehlt in diesem Projekt das Remote `dev` (`git remote get-url dev`), richte es ein: `git remote add dev https://github.com/MischaFierz/SchoolManager-dev.git`.
4. Lies `../SchoolManager-dev/notizen/CLAUDE.md`, wenn der Ordner eben erst geklont wurde oder der Pull darin etwas geändert hat - die Einbindung unten lädt nur den Stand vom Sitzungsbeginn. Sag dem Nutzer in einem Satz, was du eingerichtet hast.

@../SchoolManager-dev/notizen/CLAUDE.md
