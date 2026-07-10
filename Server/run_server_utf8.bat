@echo off
REM ============================================================
REM  Interflow headless/dedicated server launcher
REM  Sets the console to UTF-8 (code page 65001) so Russian
REM  Debug.Log output renders correctly instead of mojibake.
REM
REM  NOTE: for Cyrillic glyphs use Windows Terminal (or a console
REM  with a TrueType font: Cascadia Mono / Consolas). The legacy
REM  raster "Terminal" font will NOT show Cyrillic even at 65001.
REM
REM  Keep this .bat next to Interflow.exe (it launches the exe
REM  in its own folder via %~dp0).
REM ============================================================

chcp 65001 >nul
cd /d "%~dp0"
title Interflow Server (UTF-8)

"%~dp0Interflow.exe" -batchmode -nographics -logFile -

echo.
echo [server exited, code %errorlevel%]
pause
