@echo off
rem Baut School Manager und packt daraus ein Installationspaket.
rem Ergebnis: publish\SchoolManagerSetup.msi
rem
rem Voraussetzung (einmalig):  dotnet tool install --global wix

setlocal
cd /d "%~dp0"

echo [1/2] Anwendung veroeffentlichen ...
call "%~dp0publish.cmd"
if errorlevel 1 exit /b 1

where wix >nul 2>nul
if errorlevel 1 (
  echo.
  echo Das WiX-Werkzeug fehlt. Einmalig installieren mit:
  echo     dotnet tool install --global wix
  exit /b 1
)

rem Version aus SchoolManager.App.csproj lesen, damit Installationspaket und
rem Anwendung immer denselben Stand tragen (wichtig fuer die Update-Erkennung).
for /f "delims=" %%v in ('dotnet msbuild SchoolManager.App\SchoolManager.App.csproj -nologo -getProperty:Version') do set VERSION=%%v

echo.
echo [2/2] Installationspaket bauen (Version %VERSION%) ...
wix build installer\SchoolManager.wxs ^
  -d ExeFile="%cd%\publish\SchoolManager.exe" ^
  -d IconFile="%cd%\SchoolManager.App\app.ico" ^
  -d Version="%VERSION%" ^
  -pdbtype none ^
  -o publish\SchoolManagerSetup.msi

if errorlevel 1 (
  echo.
  echo Das Installationspaket konnte nicht gebaut werden.
  exit /b 1
)

echo.
echo Fertig: "%cd%\publish\SchoolManagerSetup.msi"
echo Doppelklick installiert School Manager ohne Administratorrechte.
endlocal
