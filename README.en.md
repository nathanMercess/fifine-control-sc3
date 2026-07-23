# FifineControl — Windows audio proof of concept

This is the Level 1 (software-only) backend for controlling the Windows audio endpoints exposed by a FIFINE AmpliGame SC3 or another USB audio device. It deliberately does not assume that the physical mute button, LEDs, pads, or knobs expose USB control messages.

## What works

- Enumerates active Windows Core Audio capture and render endpoints through NAudio.
- Reads and changes endpoint mute and master volume, then reads the resulting value back.
- Reads the endpoint peak meter in real time.
- Captures an active input endpoint to WAV.
- Writes recordings as `.wav.partial`, closes the WAV header, and only then renames to `.wav`.
- Repairs common interrupted RIFF/WAV recordings and preserves them as `_recovered.wav`.
- Stores validated profiles atomically in JSON.
- Writes structured JSON Lines diagnostic logs.
- Provides a dark WPF desktop interface with system tray support.
- Connects to the OBS WebSocket 5.x protocol and starts/stops OBS recording.
- Registers configurable Windows global hotkeys for endpoint mute and local WAV recording.
- Routes capture audio through digital gain, noise gate, compressor, and three-band parametric EQ to a selected render endpoint.
- Shows pre- and post-DSP meters in the WPF app and exposes a bounded routing command in the CLI.
- Optionally starts with the current Windows user and safely manages completed recent recordings.

Endpoint mute is a Windows software mute. Whether the SC3 reflects it on a hardware LED—and whether its physical mute changes the Windows property—must be measured on the actual unit.

## Build and run

Requires Windows and .NET 8 SDK or newer.

```powershell
dotnet restore .\FifineControl.sln
dotnet build .\FifineControl.sln -c Release
dotnet test .\FifineControl.sln -c Release --no-build
dotnet run --project .\src\FifineControl.App -c Release
dotnet run --project .\src\FifineControl.Cli -- devices
```

A framework-dependent Windows build is available at
`artifacts\publish\FifineControl\FifineControl.exe`. This machine already has the
required .NET 8 Windows Desktop runtime. To recreate it:

```powershell
dotnet publish .\src\FifineControl.App\FifineControl.App.csproj -c Release --no-restore -o .\artifacts\publish\FifineControl
```

The desktop app lets you select capture and render endpoints, control the Windows capture endpoint mute and volume, watch the live peak meter, record WAV files, manage profile state, open recent recordings, and copy diagnostics. Minimizing can hide the window in the notification area; double-click its tray icon to restore it.

## OBS and global hotkeys

In OBS Studio, enable the WebSocket server under **Tools > WebSocket Server Settings**. The app defaults to `ws://127.0.0.1:4455`, the standard port for OBS WebSocket 5.x. Enter the OBS password in the app and click **Conectar**; the password is kept only in process memory, is not logged, and is never written to `settings.json`. The server address and connect-on-start option are persisted.

The default system-wide shortcuts are `Ctrl+Shift+M` for endpoint mute and `Ctrl+Shift+R` for local WAV recording. They use the Windows `RegisterHotKey` API and are unregistered when the app exits. Their modifier flags and virtual-key values can be changed in `%LOCALAPPDATA%\FifineControl\settings.json` under `hotkeys`; restart the app after editing. If another application already owns a shortcut, the UI reports its Win32 registration error.

Discord is intentionally not controlled through an application-specific integration. Select the SC3 capture endpoint directly in Discord, or select the output of a separately installed virtual-audio-cable/virtual-endpoint route. FifineControl does not install a virtual audio driver.

Protocol references: [OBS WebSocket project](https://github.com/obsproject/obs-websocket) and [official 5.x protocol](https://github.com/obsproject/obs-websocket/blob/master/docs/generated/protocol.md).

## Startup and recent recordings

**Iniciar com o Windows** is opt-in and disabled unless the user selects it. It writes only the `FifineControl` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, using a quoted absolute executable path, so it does not require administrator rights. Turning the option off removes only that value and leaves every other startup entry untouched.

The recent-recordings card can open or rename a selected completed `.wav`. Names are sanitized, directory components are rejected, and an existing destination is never overwritten. **Mover para Lixeira** first shows a Yes/No confirmation defaulting to No, then accepts only a selected regular `.wav` directly inside the configured recording directory. It rejects reparse points and never manages `.partial` files; deletion is recoverable through the Windows Recycle Bin.

The labels distinguish the two implemented controls: **Volume do endpoint (Windows)** changes the Core Audio endpoint, while **Ganho digital da rota** is applied only while the WASAPI capture-to-render route is active. The selected monitoring endpoint is the route destination. FifineControl does not install a virtual audio driver, so routing into Discord or OBS requires a separately installed virtual endpoint when those applications must consume the processed signal.

Copy the full capture endpoint ID printed for **Mixer SC3**, including braces, and quote it:

```powershell
dotnet run --project .\src\FifineControl.Cli -- status "<endpoint-id>"
dotnet run --project .\src\FifineControl.Cli -- toggle "<endpoint-id>"
dotnet run --project .\src\FifineControl.Cli -- monitor "<endpoint-id>" 30
dotnet run --project .\src\FifineControl.Cli -- record "<endpoint-id>" 15 ".\recordings" "sc3-test"
dotnet run --project .\src\FifineControl.Cli -- route "<capture-id>" "<render-id>" 30 0
```

Other commands are shown by `dotnet run --project .\src\FifineControl.Cli -- help`. Settings and logs are stored under `%LOCALAPPDATA%\FifineControl`.

## Architecture

- `FifineControl.Core/Audio`: Core Audio endpoint enumeration, mute, volume, and metering.
- `FifineControl.Core/Dsp`: digital gain, noise gate, compressor, three-band parametric EQ, and pre/post meters.
- `FifineControl.Core/Routing`: bounded WASAPI capture-to-render routing with live DSP parameter updates and route-safety checks.
- `FifineControl.Core/Recording`: WASAPI capture and crash-aware WAV file lifecycle.
- `FifineControl.Core/Configuration`: profiles and validated atomic persistence.
- `FifineControl.Core/Integrations/Obs`: native WebSocket client for OBS protocol authentication and recording requests.
- `FifineControl.Core/Hotkeys`: configurable `RegisterHotKey` lifecycle and message dispatch.
- `FifineControl.Core/Startup`: current-user startup registration isolated behind a registry abstraction.
- `FifineControl.Core/Logging`: low-dependency structured file logging.
- `FifineControl.Cli`: executable diagnostic/control surface suitable for validating the real SC3 before a GUI is added.
- `FifineControl.App`: WPF desktop UI, MVVM state/commands, recording timer, recent files, diagnostics, and tray lifecycle.

The interfaces keep endpoint access, routing/DSP, recording, hotkeys, OBS, and the read-only HID investigation isolated. Decoding the vendor HID protocol—and deciding whether a virtual-audio driver is ever justified—remain future work.

## Safe SC3 validation sequence

1. Run `devices` and identify all endpoints whose name contains `Mixer SC3`.
2. Run `status` for each endpoint and save the output.
3. Run `monitor` on the capture endpoint and speak into the microphone.
4. Press the physical mute button while monitoring. Note whether the meter drops and run `status` again to see whether the Windows mute property changed.
5. Run `toggle`; verify the captured signal and physical LED independently.
6. Make a short recording and play it in a trusted player.

No firmware, USB descriptors, drivers, or hardware state are modified by this PoC.
