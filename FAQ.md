# FAQ & Troubleshooting

Quick answers for common questions and setup hiccups—check here before opening an issue.

## General

### Do I still need the official GoXLR App?
No. Close and uninstall the legacy `GoXLR App.exe`—only one app can hold the USB handle at a time. **Do keep** the **TC-Helicon GoXLR Audio Driver** installed, though, because Project XLR and Windows still rely on it.

### Does this work with the full-size GoXLR?
Project XLR is built and tested for the **GoXLR Mini**. Full-size-only hardware features (motorized faders, sampler banks, and hardware voice FX) aren't implemented.

### Does this work on macOS or Linux?
No—Windows 10/11 (64-bit) only. It uses WPF and talks directly to the Windows TC-Helicon / Thesycon driver DLL (`goxlr_audioapi_x64.dll`). If you're on Linux, check out `goxlr-utility`.

### Will it import my old GoXLR profiles?
Yes. On first launch (when no Project XLR profiles exist yet), or from **Settings**, Project XLR can import your existing official GoXLR profiles.

### Why did Windows SmartScreen warn me on `Install.bat` / `ProjectXLR.exe`?
Release binaries aren't signed with a paid Windows code-signing certificate, so SmartScreen flags them on first launch. Click **More info → Run anyway**, or build directly from source with `dotnet build`.

### Why didn't clicking `X` close the app?
Closing the window minimizes Project XLR to the system tray so fader sync, lighting, and audio monitoring keep running in the background. Use **Settings → Exit** (or right-click the tray icon) to quit completely.

---

## Troubleshooting

### Why does it say "GoXLR not connected"?
* **Legacy GoXLR App is still running:** Check the system tray or Task Manager for `GoXLR App.exe`, close it, and click **Reconnect**.
* **Missing driver:** Make sure the official **TC-Helicon GoXLR Audio Driver** is installed (`C:\Program Files\TC-Helicon\GoXLR_Audio_Driver\W10_x64\goxlr_audioapi_x64.dll`).
* **USB hub drop-outs:** Plug the GoXLR straight into a rear motherboard USB port instead of an unpowered hub or front-panel header.

### Why can't Discord/games hear my mic, or why is there no sound in my headphones?
* **Check your input device:** Set Windows, Discord, and games to use **`Chat Mic (TC-Helicon GoXLR Mini)`** as the microphone.
* **Run the quick audio check:** Open **Settings → Check my audio setup** and click **Check now**.
* **Check your Routing matrix:** Verify **Microphone** is routed to **Voice chat**, and desktop/game/music channels are *not* routed to **Voice chat**.
* **Check mutes & faders:** Make sure **Mic** and **Headphones** aren't muted in **Mixer** or via the physical Cough/Mute buttons.

### Why is there a slight delay when "Hear myself" is on with an external USB DAC?
When you use an external USB DAC instead of the GoXLR's headphone jack, **Hear myself** captures `Chat Mic` (after AI noise removal) and routes it to your DAC over WASAPI, which adds a small software buffer delay. For zero-latency hardware sidetone, plug headphones straight into the GoXLR's 3.5 mm **Headphones** jack.

### DeepFilterNet 3 / AI noise removal isn't working (or broke after a Windows Update)
* **"Not Installed":** Equalizer APO and Alt-Denoiser aren't bundled in the repo or release zip. Grab **[Equalizer APO (x64)](https://sourceforge.net/projects/equalizerapo/)** and **[Alt-Denoiser (Windows)](https://github.com/Altinus/Alt-Denoiser/releases)**, then run `tools\Setup-DeepFilterNet.bat` (or click **Configure / Repair DeepFilterNet 3** in **Settings**).
* **"Chat Mic Needs Device Hook":** Click **Configurator** under **Settings → AI noise removal**, open the **Capture devices** tab, check **`Chat Mic (TC-Helicon GoXLR Mini)`**, click **OK**, and reboot if prompted.
* **"Plugin File Missing":** Make sure `Alt Denoiser.dll` is in `C:\Program Files\EqualizerAPO\VSTPlugins\`, then click **Refresh**.
* **Stopped working after a Windows Update:** Major Windows updates often reset Equalizer APO hooks. Re-open **Configurator**, re-check `Chat Mic`, and reboot if prompted.
* **Does AI noise removal work in Discord?** No—because of how Discord captures microphone audio and handles audio processing, DeepFilterNet 3 / Equalizer APO noise suppression does not apply to Discord. Use Discord's built-in noise suppression (Krisp) for Discord; Project XLR's AI filter still handles games and other apps.

### My voice cuts out when I talk quietly, or keyboard clicks still come through
* Run **Voice → Check my voice** from your normal seating distance so Project XLR can measure your room floor, keyboard clicks, and quiet speech.
* Avoid cranking the hardware noise gate to hide loud mechanical keys; pair a lighter hardware gate with **AI noise removal** (DeepFilterNet 3) instead.

### Why didn't moving a slider in the app snap back to the physical fader position?
That's intentional. Editing a fader in the UI pauses hardware ADC polling for that slider until you nudge the physical slider past a small deadzone, so analog potentiometer jitter won't fight your mouse.
