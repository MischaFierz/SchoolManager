@echo off
rem Baut SchoolManager.exe als eine einzige, eigenstaendige Datei.
rem Ergebnis: publish\SchoolManager.exe - laeuft ohne installiertes .NET.

setlocal
cd /d "%~dp0"

dotnet publish SchoolManager.App\SchoolManager.App.csproj ^
  --configuration Release ^
  --runtime win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=none ^
  --output publish

if errorlevel 1 (
  echo.
  echo Der Build ist fehlgeschlagen.
  exit /b 1
)

echo.
echo Fertig: "%cd%\publish\SchoolManager.exe"
endlocal
