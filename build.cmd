@echo off
rem Builds the app into the "dist" folder.
cd /d "%~dp0"
dotnet publish src\CanvasExplorer -c Release -r win-x64 --self-contained false -o dist -nologo -v q
if errorlevel 1 (
  echo.
  echo Build failed.
  pause
  exit /b 1
)
echo Build OK: %~dp0dist\CanvasExplorer.exe
