@echo off
REM Automatically close any prior instance holding the USB device lock
taskkill /F /IM "GoXLR App.exe" >nul 2>&1
taskkill /F /IM "ProjectXLR.exe" >nul 2>&1

if exist "%~dp0src\ProjectXLR\bin\Release\net9.0-windows\ProjectXLR.exe" (
    cd /d "%~dp0src\ProjectXLR\bin\Release\net9.0-windows"
    start "" "ProjectXLR.exe"
) else (
    cd /d "%~dp0src\ProjectXLR\bin\Debug\net9.0-windows"
    start "" "ProjectXLR.exe"
)
