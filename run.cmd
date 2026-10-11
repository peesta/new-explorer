@echo off
rem Rebuilds (fast if nothing changed) and starts the app. Optional argument: folder to open.
cd /d "%~dp0"
taskkill /IM CanvasExplorer.exe /F >nul 2>&1
call build.cmd || exit /b 1
start "" "%~dp0dist\CanvasExplorer.exe" %*
