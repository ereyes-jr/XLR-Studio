# GoXLR App: reverse-engineering notes

## Findings so far (static analysis)
- `GoXLR App.exe` v1.6.4.014: **32-bit**, built with the **JUCE** C++ framework (~650 JUCE strings), build timestamp 2024-06-10.
- No USB code of its own. It loads the TC-Helicon/**Thesycon** driver API `goxlr_audioapi.dll` at runtime (`GetProcAddress` on 60+ `TUSBAUDIO_*` functions).
- Device control goes through `TUSBAUDIO_ClassVendorRequestOut/In`, which carry TC-Helicon's proprietary command protocol (called "DCP" in the app's error messages).
- Other parts: `TCLoudnessProc.dll` (loudness meter), WinINet (update checks), WinSock (local server for the Stream Deck plugin), WinMM MIDI, and OpenGL (UI rendering).
- Settings: JUCE `PropertiesFile` XML in `%APPDATA%\TC-Helicon\GoXLR`. Profiles are zip files (`profile.xml` + scribble PNGs).

## Dynamic analysis
- `tools/hook_tusb.js` + `tools/capture.py`: a Frida hook that logs every driver API call (read-only).
- The app makes **no** device calls while idle. Traffic only happens on user or device events, so captures must be done while performing actions.
