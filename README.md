# AstroDesk

AstroDesk is a Windows 11 shooting companion for phone-based astrophotography. It is designed around a Samsung Galaxy S23 Ultra on a tripod, connected by USB to a Windows laptop, with Samsung Camera providing the camera controls and [scrcpy](https://github.com/Genymobile/scrcpy) providing the mirrored device session.

AstroDesk keeps the practical shooting workflow in one dark, local-first desktop application:

- an embedded phone preview with mouse and keyboard input forwarding;
- ADB connection and phone-status monitoring;
- shooting-session, frame-count, exposure, notes, and history tools;
- non-destructive framing overlays, zoom, preview screenshots, and histograms;
- weather, moon, and twilight context; and
- local SQLite storage and portable session exports.

AstroDesk has no AI features, accounts, analytics, advertising, or telemetry.

> [!IMPORTANT]
> AstroDesk is currently a pre-1.0 project. The capture, input, ADB, scrcpy, persistence, and calculation paths are covered by automated tests where practical, but this repository has not been validated here with the physical S23 Ultra/ThinkPad setup. Treat embedded capture, hidden-window behavior, and direct input forwarding as requiring hardware validation before relying on them during an unattended session.

## Screenshots

> **Screenshot placeholder — Main shooting workspace**
>
> Replace this block with a Windows 11 capture showing the conditions panel, embedded phone preview, session panel, and bottom shooting toolbar after hardware validation.

> **Screenshot placeholder — Full red night mode**
>
> Replace this block with a capture demonstrating the red/black UI and preview tint. The tint must affect only the laptop display.

> **Screenshot placeholder — Session history and export**
>
> Replace this block with a capture showing filters, session details, notes, conditions, and preview screenshots.

## What is in the first release

The repository contains the production foundations for:

- .NET 8 WPF with strict nullable reference types and MVVM-oriented separation;
- local SQLite storage through Entity Framework Core migrations;
- session lifecycle, manual frame tracking, exposure calculations, notes, condition snapshots, and exports;
- ADB device discovery, unauthorized/offline handling, status parsing, reconnect behavior, and serial-number log redaction;
- scrcpy discovery, launch, unique-window matching, redirected output handling, crash detection, and off-screen window management;
- aspect-ratio-preserving preview rendering, DPI-aware coordinate mapping, rotation mapping, overlays, preview zoom, freeze inspection, and a focus magnifier;
- direct Win32 mouse/keyboard message forwarding with lower-frequency ADB input fallbacks;
- bounded frame delivery, throttled OpenCV histograms, and PNG preview screenshots;
- no-key Open-Meteo weather and location search providers;
- local moon and twilight calculations through Astronomy Engine; and
- local rolling logs with retention and size limits.

Some controls and integrations remain experimental or need end-to-end hardware verification. See [Known limitations](#known-limitations), [ROADMAP.md](ROADMAP.md), and [.github/ISSUES_TO_CREATE.md](.github/ISSUES_TO_CREATE.md).

## Requirements

### To run a published build

- Windows 11, x64.
- A USB data cable and an Android phone with USB debugging enabled.
- Android SDK Platform-Tools, including `adb.exe`.
- A recent stable scrcpy build whose command line supports `--window-title`, `--no-audio`, `--video-bit-rate`, `--max-size`, `--max-fps`, and `--stay-awake`.

The release workflow creates a self-contained `win-x64` build, so the .NET runtime is not required for that package. The portable package is currently unsigned and does not include an installer.

### To build from source

- Windows 11.
- Visual Studio 2022 with the .NET desktop development workload, or the .NET 8 SDK.
- .NET SDK `8.0.423`, as selected by [`global.json`](global.json). A later patch in the compatible SDK feature band is accepted by that file.
- Git.
- ADB and scrcpy for device-integration testing.

## Prepare the phone

1. On the phone, open **Settings > About phone > Software information**.
2. Tap **Build number** seven times and confirm the device PIN to enable Developer options.
3. Open **Settings > Developer options** and enable **USB debugging**.
4. Connect the phone with a known-good USB data cable.
5. Unlock the phone and accept the USB debugging authorization prompt. Selecting **Always allow from this computer** is optional.
6. Verify the connection:

   ```powershell
   adb devices -l
   ```

An authorized device is listed with the state `device`. If it is listed as `unauthorized`, unlock the phone and accept the RSA prompt. If the prompt does not appear, revoke USB debugging authorizations on the phone, disconnect and reconnect the cable, and run `adb devices -l` again.

AstroDesk detects multiple authorized devices and provides a preferred-device selector instead of choosing one ambiguously. The S23 Ultra is the primary target, but the device layer does not intentionally hard-code a serial number or a single Android model.

## Install and locate ADB and scrcpy

Install Android SDK Platform-Tools and scrcpy from their official distribution channels. Keep both tools updated together with their documented dependencies.

AstroDesk resolves the executables from:

1. a user-configured executable path;
2. `ASTRODESK_ADB_PATH` or `ASTRODESK_SCRCPY_PATH`;
3. the application and current directories; and
4. the Windows `PATH`.

The environment variables may point directly to the executable. For a temporary PowerShell session:

```powershell
$env:ASTRODESK_ADB_PATH = "C:\Tools\platform-tools\adb.exe"
$env:ASTRODESK_SCRCPY_PATH = "C:\Tools\scrcpy\scrcpy.exe"
```

No tool path is hard-coded into the repository.

## Build, test, and run

From the repository root:

```powershell
dotnet restore AstroDesk.sln
dotnet build AstroDesk.sln --configuration Release --no-restore
dotnet test AstroDesk.sln --configuration Release --no-build
dotnet run --project src/AstroDesk.App/AstroDesk.App.csproj
```

If `dotnet` is not on `PATH`, the standard Windows installation can be invoked as:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build AstroDesk.sln --configuration Release
```

For development and database-migration details, see [DEVELOPMENT.md](DEVELOPMENT.md).

## Publish a portable Windows build

```powershell
dotnet publish src/AstroDesk.App/AstroDesk.App.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output artifacts/publish/win-x64
```

Run `artifacts\publish\win-x64\AstroDesk.exe`. The GitHub release workflow performs the same style of self-contained publish, packages the output with the license and release notes, and generates a SHA-256 checksum.

There is currently no MSI/MSIX installer and no code-signing certificate in the repository. Do not add certificates, private keys, API tokens, or signing passwords to source control.

## How embedded preview and control work

AstroDesk starts scrcpy as a child process with a unique title, audio disabled, configurable bitrate/resolution/FPS, and optional stay-awake or screen-off flags. After finding the exact window belonging to that process, AstroDesk removes its normal taskbar presentation and moves it off-screen without minimizing it.

The current capture implementation uses a suitable Win32 window-capture abstraction backed by `PrintWindow`, with `BitBlt` as a fallback. It does **not** claim to use Windows Graphics Capture today. The abstraction allows a Windows Graphics Capture backend to be added later without changing the preview, histogram, screenshot, or mapping consumers.

Mouse and keyboard events from the WPF preview are mapped into the real scrcpy client area. The mapper accounts for WPF device-independent pixels, monitor DPI, letterboxing, captured-frame size, scrcpy client size, sizing mode, zoom crop, and rotation. Direct Win32 messages are preferred for responsiveness. ADB `input` commands are used only when a supported direct action fails and an authorized device is selected.

Detailed behavior is documented in:

- [SCRCPY_INTEGRATION.md](SCRCPY_INTEGRATION.md)
- [INPUT_MAPPING.md](INPUT_MAPPING.md)
- [ARCHITECTURE.md](ARCHITECTURE.md)

## Local data

By default AstroDesk stores application data under:

```text
%LOCALAPPDATA%\AstroDesk\
├── astrodesk.db
├── Logs\
├── Preview Screenshots\
└── Sessions\
    └── yyyy-MM-dd_Type_Target_Location_shortId\
        ├── session.json
        ├── notes.txt
        ├── screenshots\
        └── export\
            ├── session.json
            └── summary.md
```

SQLite remains the source of truth. Session folders contain portable exports and preview assets. Preview screenshots are images of the mirrored scrcpy view, not full-resolution photographs from Samsung Camera.

Before copying or backing up the live SQLite database, close AstroDesk and keep `astrodesk.db`, `astrodesk.db-wal`, and `astrodesk.db-shm` together if the sidecar files exist.

## Weather, astronomy, and location

- Optional weather and location search use Open-Meteo and require internet access, but no paid key or account. They are disabled by default.
- Moon and twilight calculations run locally with Astronomy Engine.
- Manual coordinates and seeded Lebanon examples are supported without restricting the app to Lebanon.
- Current-device location is unavailable until a Windows location source is implemented and permission is granted.
- Missing or failed provider data is shown as `Unavailable`; AstroDesk does not silently substitute fake observations.

## Experimental ADB shutter control

The exposure timer is a shooting assistant by default. It does not automatically press the Samsung Camera shutter.

Optional shutter control through a user-configured ADB tap coordinate is experimental and must remain explicitly disabled until the user opts in and verifies the coordinate. Camera layout, phone rotation, display scaling, app updates, and accidental touches can make a saved coordinate unsafe or incorrect. Never use this feature unattended until it has been tested with the exact phone orientation and Camera layout.

AstroDesk does not claim to automatically detect every photo taken by Samsung Camera. The frame counter is manual in the first release.

## Troubleshooting

### `adb` or `scrcpy` cannot be found

- Set the executable path in AstroDesk when that setting is available in the current build.
- Set `ASTRODESK_ADB_PATH` and `ASTRODESK_SCRCPY_PATH`.
- Or add the tool directories to `PATH`.
- Confirm the paths point to real executable files and are not blocked by Windows.

### Device is `unauthorized`

- Unlock the phone and accept the RSA prompt.
- Try another USB data cable or port.
- Set the USB mode to data transfer if Windows is not detecting the phone.
- Revoke USB debugging authorizations, reconnect, and authorize again.
- Confirm Samsung's Windows USB driver is installed if ADB cannot see the device.

### More than one device is connected

Select the intended device in AstroDesk. At the command line, use `adb devices -l` to identify the devices. Do not publish logs containing raw device serial numbers.

### scrcpy starts but AstroDesk cannot find its window

- Start scrcpy manually to confirm it works with the selected device.
- Check that another wrapper is not changing the window behavior.
- Review `%LOCALAPPDATA%\AstroDesk\Logs`.
- Confirm the installed scrcpy accepts the configured command-line flags.
- Retry after closing orphaned scrcpy processes.

### The embedded preview is black, frozen, or stops updating

- Restore or restart scrcpy and reconnect the device.
- Keep the real scrcpy window unminimized; minimizing can stop some capture paths.
- Test on the laptop's primary display and with standard DPI scaling.
- Disable overlays and histogram temporarily while isolating performance.
- Some GPU, display-driver, or scrcpy combinations may not render through `PrintWindow`/`BitBlt`. This is a known reason to add a Windows Graphics Capture backend.

### Pointer input is offset

- Reset preview zoom and use fit mode.
- Confirm the phone orientation and captured frame dimensions have updated.
- Move AstroDesk between monitors only after the current DPI change completes.
- Enable the debug overlay and record embedded coordinates, mapped client coordinates, frame size, client size, DPI scale, and rotation.
- See [INPUT_MAPPING.md](INPUT_MAPPING.md) before filing a report.

### Weather is unavailable

Enable online conditions in Settings before using Open-Meteo weather or location search. Those features require internet access; astronomy calculations remain local. Provider failure should not stop the session workflow.

### Database startup fails

Do not immediately delete the database. Close AstroDesk, back up the database together with any `-wal` and `-shm` files, verify free disk space and folder permissions, then review the local log. The initializer performs an integrity check and reports recovery-oriented errors.

## Privacy

AstroDesk keeps its database, session records, phone status, screen captures, notes, logs, and photographs local. It has no account system, cloud synchronization, analytics, or telemetry.

Online weather and location search are opt-in and disabled by default. When enabled, an Open-Meteo weather request sends the selected latitude and longitude, while location search sends the entered search text to Open-Meteo's geocoding service. AstroDesk does not send preview frames, screenshots, session contents, phone status, logs, or device serials with those requests. Astronomy calculations are local. Review [SECURITY.md](SECURITY.md) for provider, USB-debugging, and external-executable trust boundaries.

## Known limitations

- The physical S23 Ultra/ThinkPad workflow was not available for validation in this development environment.
- The current embedded capture backend is `PrintWindow` with `BitBlt` fallback behind an abstraction, not Windows Graphics Capture.
- Hidden/off-screen scrcpy capture and direct Win32 input can vary with scrcpy, SDL, GPU, display driver, Windows scaling, and multi-monitor configuration.
- ADB input fallback is less responsive than direct scrcpy-window forwarding.
- ADB fallback scaling uses the phone's reported physical resolution when available and remains best-effort when that value is unavailable.
- The frame counter is manual; automatic Samsung Camera shot detection is not claimed.
- ADB shutter-coordinate control is opt-in and experimental.
- Preview screenshots are not full-resolution phone photos.
- Current-device location is not implemented; manual coordinates, saved locations, and search are the supported paths.
- Existing/seeded locations can be loaded and search results used, but complete save/edit/delete management for observing locations is unfinished.
- Planned/historical astronomy views need a consistent requested-date instant for moon phase/altitude as well as twilight calculations.
- Preview “fullscreen” is an in-window distraction-free layout, not a verified borderless/exclusive Windows fullscreen mode.
- Full red mode has a visually verified red/black application palette and re-tints an already displayed preview frame. Windows still renders the system window buttons, and live phone-frame tinting remains part of hardware validation.
- History currently loads at most 500 records and does not yet provide complete editing or screenshot thumbnails.
- Normal rolling logs retain warnings/errors and Info-level diagnostics; redirected scrcpy standard output is Debug-level and is not retained by the default file logger.
- The release artifact is a portable, unsigned `win-x64` package. An installer, signing, and SmartScreen reputation are not provided.
- Phone temperature values depend on what the device exposes through ADB. Unavailable values remain `Unavailable`.
- Weather needs an internet connection; no fake fallback readings are generated.

## Documentation

- [ARCHITECTURE.md](ARCHITECTURE.md) — project boundaries, runtime flows, persistence, and failure isolation
- [DEVELOPMENT.md](DEVELOPMENT.md) — local setup, tests, migrations, packaging, and hardware validation
- [SCRCPY_INTEGRATION.md](SCRCPY_INTEGRATION.md) — child process, hidden window, capture, and recovery behavior
- [INPUT_MAPPING.md](INPUT_MAPPING.md) — DPI, letterboxing, rotation, and input delivery
- [ROADMAP.md](ROADMAP.md) — release direction and validation gates
- [CONTRIBUTING.md](CONTRIBUTING.md) — contribution workflow and quality expectations
- [SECURITY.md](SECURITY.md) — vulnerability reporting and local-data security
- [CHANGELOG.md](CHANGELOG.md) — release history

## License

AstroDesk is licensed under the [MIT License](LICENSE).
