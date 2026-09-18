<#
.SYNOPSIS
    Bereitet ein öffentliches Release vor: der ganze Entwicklungszweig als ein
    einziger Commit auf master, danach master zurück in den Entwicklungszweig.

.DESCRIPTION
    Auf master soll je Release genau ein Commit stehen - mit der Nachricht, die
    öffentlich zu sehen ist. Weil dabei die Geschichten der beiden Zweige
    auseinanderlaufen, gibt es beim nächsten Mal sonst Konflikte in Dateien, die
    beide Seiten angefasst haben. Das Skript löst sie immer gleich:

      1. master auf den Stand von origin bringen.
      2. Den Entwicklungszweig als einen Commit auf master bringen. Bei
         Konflikten gilt der Stand der Entwicklung - sie ist das Neuere.
      3. master in den Entwicklungszweig zurückführen. Danach haben beide
         denselben Inhalt, und das nächste Release beginnt ohne Konflikte.

    Geschoben und getaggt wird nichts. Das Skript zeigt am Ende die Befehle
    dafür. Vorher gehören die Version in der Projektdatei, der Changelog
    (IsPreRelease: false) und release-notes/vX.Y.Z.md auf den Entwicklungszweig.

.EXAMPLE
    .\tools\Release-Vorbereiten.ps1 -Version 1.3.0 -Nachricht "fix: ..."
#>
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    [Parameter(Mandatory)]
    [ValidatePattern('^fix: ')]
    [string] $Nachricht,

    # Ohne Angabe der Zweig, auf dem man gerade steht.
    [string] $Entwicklungszweig
)

$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)

function Invoke-Git {
    & git.exe @args
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') ist gescheitert." }
}

if (-not $Entwicklungszweig) { $Entwicklungszweig = (git.exe branch --show-current).Trim() }

if ($Entwicklungszweig -eq "master") { throw "Das Skript läuft vom Entwicklungszweig aus, nicht von master." }
if (git.exe status --porcelain) { throw "Es gibt noch offene Änderungen - erst committen." }

$projekt = Get-Content "SchoolManager.App/SchoolManager.App.csproj" -Raw
if ($projekt -notmatch "<Version>$([regex]::Escape($Version))</Version>") {
    throw "In SchoolManager.App.csproj steht nicht die Version $Version."
}

if (-not (Test-Path "release-notes/v$Version.md")) {
    Write-Warning "release-notes/v$Version.md fehlt - die Release-Notiz auf GitHub bleibt dann leer."
}

Write-Host "1/3  master auf den Stand von origin bringen" -ForegroundColor Cyan
Invoke-Git fetch origin
Invoke-Git checkout master
Invoke-Git merge --ff-only origin/master

Write-Host "2/3  $Entwicklungszweig als ein Commit auf master" -ForegroundColor Cyan
& git.exe merge --squash $Entwicklungszweig | Out-Host
$konflikte = git.exe diff --name-only --diff-filter=U
foreach ($datei in $konflikte) {
    Write-Host "     Konflikt in $datei - der Stand der Entwicklung gilt"
    Invoke-Git checkout --theirs -- $datei
    Invoke-Git add -- $datei
}
Invoke-Git commit -m $Nachricht

Write-Host "3/3  master zurück in $Entwicklungszweig" -ForegroundColor Cyan
Invoke-Git checkout $Entwicklungszweig
& git.exe merge --no-edit -m "fix: version $Version" master | Out-Host
$konflikte = git.exe diff --name-only --diff-filter=U
foreach ($datei in $konflikte) {
    Invoke-Git checkout --theirs -- $datei
    Invoke-Git add -- $datei
}
if ($konflikte) { Git commit -m "fix: version $Version" }

if (git.exe diff master $Entwicklungszweig --name-only) { throw "master und $Entwicklungszweig unterscheiden sich noch - bitte nachsehen." }

Write-Host ""
Write-Host "Fertig vorbereitet. master und $Entwicklungszweig sind gleich." -ForegroundColor Green
Write-Host "Veröffentlichen:"
Write-Host "  git push origin master"
Write-Host "  git tag v$Version"
Write-Host "  git push origin refs/tags/v$Version"
Write-Host "  git push dev $Entwicklungszweig"
Write-Host "Danach, wenn der Bau grün ist: die Dev-Versionen bis $Version aufräumen (Release und Tag)."
