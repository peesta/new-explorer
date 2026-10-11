@echo off
rem Runs the tests.
rem   test.cmd       unit tests only (fast, no UI)
rem   test.cmd ui    everything, including UI scenarios: they start the app and move the REAL mouse
rem                  and keyboard for about 2.5 minutes - do not touch them meanwhile.
cd /d "%~dp0"
if /i "%~1"=="ui" (
  dotnet test tests\CanvasExplorer.Tests -nologo
) else (
  dotnet test tests\CanvasExplorer.Tests -nologo --filter "Category!=UI"
)
