@echo off
REM Esegue lo stesso flusso di scripts\ai-pixel-scaler-flow.ps1 dalla root del repo.
REM Uso: ai-pixel-scaler-flow.cmd
REM      ai-pixel-scaler-flow.cmd publish
setlocal
cd /d "%~dp0"
if /i "%~1"=="publish" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\ai-pixel-scaler-flow.ps1" -Publish
) else (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\ai-pixel-scaler-flow.ps1"
)
exit /b %ERRORLEVEL%
