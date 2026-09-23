@echo off
rem Snipjar のインストール。このファイルをダブルクリックするだけ。
rem PowerShell を開いてコマンドを打つ必要はありません。
chcp 65001 >nul
title Snipjar のセットアップ

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1" %*
set EXITCODE=%ERRORLEVEL%

echo.
if %EXITCODE% neq 0 (
  echo セットアップが最後まで進みませんでした。上の表示を確認してください。
) else (
  echo 完了しました。このウィンドウは閉じて大丈夫です。
)
echo.
pause
