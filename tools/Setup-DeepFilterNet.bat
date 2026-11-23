@echo off
title Setup DeepFilterNet3 for GoXLR Mini (Games & Apps)
echo =====================================================================
echo  Project XLR: DeepFilterNet 3 AI Noise Suppression Installer
echo =====================================================================
echo.
echo This will configure DeepFilterNet 3 directly on "Chat Mic (GoXLR Mini)"
echo with 0 extra audio devices for games and voice apps!
echo (Note: Because of how Discord captures mic audio, APO noise suppression
echo  does not apply to Discord; keep Discord's built-in Krisp enabled there.)
echo.

:: 1. Launch Equalizer APO Installer if not installed
if not exist "C:\Program Files\EqualizerAPO\Configurator.exe" (
    echo [1/3] Launching Equalizer APO Installer...
    echo Please follow the prompts. On the 'Configurator' screen:
    echo   - Click the 'Capture devices' tab
    echo   - Check the box for 'Chat Mic (TC-Helicon GoXLR Mini)'
    echo   - Click OK
    echo.
    "%~dp0EqualizerAPO\EqualizerAPO-x64-1.4.2.exe"
) else (
    echo [1/3] Equalizer APO is already installed!
)

:: 2. Create VST folder and copy Alt-Denoiser (DeepFilterNet 3)
echo [2/3] Installing DeepFilterNet 3 VST plugin...
if not exist "C:\Program Files\EqualizerAPO\VSTPlugins" (
    mkdir "C:\Program Files\EqualizerAPO\VSTPlugins" 2>nul
)

copy /Y "%~dp0Alt-Denoiser\Alt-Denoiser-windows\VST\Alt Denoiser.dll" "C:\Program Files\EqualizerAPO\VSTPlugins\Alt Denoiser.dll" >nul
if exist "%~dp0Alt-Denoiser\Alt-Denoiser-windows\VST3\Alt Denoiser.vst3" (
    if not exist "C:\Program Files\Common Files\VST3" mkdir "C:\Program Files\Common Files\VST3" 2>nul
    xcopy /E /I /Y "%~dp0Alt-Denoiser\Alt-Denoiser-windows\VST3\Alt Denoiser.vst3" "C:\Program Files\Common Files\VST3\Alt Denoiser.vst3" >nul
)

:: 3. Configure config.txt for Chat Mic
echo [3/3] Writing Equalizer APO config for Chat Mic...
(
    echo # Project XLR - DeepFilterNet 3 Neural AI Noise Suppression
    echo # Eliminates mechanical keyboard clatter for games & voice apps
    echo Device: "Chat Mic"
    echo VSTPlugin: Library "C:\Program Files\EqualizerAPO\VSTPlugins\Alt Denoiser.dll"
) > "C:\Program Files\EqualizerAPO\config\config.txt" 2>nul

echo.
echo =====================================================================
echo  SUCCESS! DeepFilterNet 3 is configured on Chat Mic (GoXLR Mini)!
echo =====================================================================
echo.
echo In games and voice apps:
echo   - Keep your Input Device set to: "Chat Mic (TC-Helicon GoXLR Mini)"
echo   - Note: Discord bypasses Equalizer APO mic processing, so keep Discord's
echo     built-in Krisp noise suppression enabled when using Discord.
echo   - Mechanical keyboard clatter is filtered for games and other apps!
echo.
pause
