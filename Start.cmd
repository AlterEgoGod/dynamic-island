@echo off
cd /d "%~dp0"
if not exist "dist\DynamicIsland.exe" (
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "build.ps1"
  if errorlevel 1 (
    pause
    exit /b 1
  )
)
start "" "dist\DynamicIsland.exe"
