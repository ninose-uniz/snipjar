@echo off
rem Snipjar installer - just double-click this file.
rem It runs setup.ps1 with the execution policy handled.
setlocal
chcp 65001 >nul
title Snipjar setup

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1" %*
set "EXITCODE=%ERRORLEVEL%"

echo.
if not "%EXITCODE%"=="0" (
  echo Setup did not finish. Scroll up to see what happened.
) else (
  echo Done. You can close this window.
)
echo.
pause
endlocal
