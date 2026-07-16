# AstroDesk development guide

This guide covers source setup, build verification, database migrations, device-integration checks, and packaging for the .NET 8 WPF solution.

## Prerequisites

- Windows 11 x64
- Git
- .NET SDK `8.0.423`, selected by `global.json`
- Visual Studio 2022 with the .NET desktop development workload, or another editor with C# support
- Android SDK Platform-Tools for ADB integration work
- A recent stable scrcpy release for preview/control integration work

The WPF project targets `net8.0-windows10.0.19041.0`. The repository is intended to build and run on Windows; CI therefore uses a Windows runner.

Verify the SDK:

```powershell
dotnet --info
dotnet --version
```

If `dotnet` is installed in the standard location but is not on `PATH`:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" --info
```

## Restore, format, build, and test

Run commands from the repository root:

```powershell
dotnet restore AstroDesk.sln
dotnet format AstroDesk.sln --no-restore
dotnet build AstroDesk.sln --configuration Release --no-restore
dotnet test AstroDesk.sln --configuration Release --no-build
```

To verify formatting without modifying files:

```powershell
dotnet format AstroDesk.sln --no-restore --verify-no-changes
```

Warnings are treated as errors for production projects. Nullable reference types are enabled across the solution. Test projects relax general warning-as-error behavior where needed but retain nullable checking.

Useful focused commands:

```powershell
dotnet test tests/AstroDesk.Core.Tests/AstroDesk.Core.Tests.csproj
dotnet test tests/AstroDesk.Device.Tests/AstroDesk.Device.Tests.csproj
dotnet test tests/AstroDesk.Capture.Tests/AstroDesk.Capture.Tests.csproj
```

## Run the application

```powershell
dotnet run --project src/AstroDesk.App/AstroDesk.App.csproj
```

For device work, first verify the external tools independently:

```powershell
adb version
adb devices -l
scrcpy --version
scrcpy --window-title="AstroDesk-Manual-Test" --no-audio
```

Stop the manual scrcpy process before testing AstroDesk's managed child process.

## External tool configuration

Executable lookup is intentionally machine-independent. Resolution checks:

1. the user/configured executable path;
2. `ASTRODESK_ADB_PATH` or `ASTRODESK_SCRCPY_PATH`;
3. the application base directory;
4. the current directory; and
5. `PATH`.

Example for one PowerShell process:

```powershell
$env:ASTRODESK_ADB_PATH = "C:\Tools\platform-tools\adb.exe"
$env:ASTRODESK_SCRCPY_PATH = "C:\Tools\scrcpy\scrcpy.exe"
dotnet run --project src/AstroDesk.App/AstroDesk.App.csproj
```

Do not commit local paths. `appsettings.Local.json` and `.env` are ignored, although AstroDesk currently requires no provider secrets or environment file.

## Configuration

Non-secret defaults are in `src/AstroDesk.App/appsettings.json`, including:

- phone-status refresh interval;
- weather refresh interval;
- automatic scrcpy startup/reconnect choices;
- scrcpy bitrate, maximum dimension, and maximum FPS;
- default exposure and frame count;
- default observing location;
- night mode; and
- the experimental capture-control switch; and
- the opt-in network conditions/geocoding switch, disabled by default.

Configuration changes must fail clearly when values are out of range. Never place credentials, device serials, private coordinates, signing material, or user-specific filesystem paths in committed configuration.

## Application data during development

The default root is:

```text
%LOCALAPPDATA%\AstroDesk
```

It contains:

- `astrodesk.db`
- `Logs\`
- `Preview Screenshots\`
- `Sessions\`

For isolated tests or manual experiments, prefer injecting a temporary data root rather than deleting the normal user database.

To inspect a database safely:

1. close AstroDesk;
2. copy `astrodesk.db` and any `-wal`/`-shm` sidecars together;
3. inspect the copy with a SQLite tool; and
4. never commit the database.

## Entity Framework Core migrations

The committed migrations live in `src/AstroDesk.Data/Migrations`.

Install the matching EF tool if migration work is required:

```powershell
dotnet tool install --global dotnet-ef --version 8.0.29
```

Create a migration:

```powershell
dotnet ef migrations add MigrationName `
  --project src/AstroDesk.Data/AstroDesk.Data.csproj `
  --startup-project src/AstroDesk.App/AstroDesk.App.csproj `
  --context AstroDeskDbContext
```

Review every generated migration and model snapshot. Then run:

```powershell
dotnet build AstroDesk.sln --configuration Release
dotnet test tests/AstroDesk.Core.Tests/AstroDesk.Core.Tests.csproj --configuration Release
dotnet ef migrations has-pending-model-changes `
  --project src/AstroDesk.Data/AstroDesk.Data.csproj `
  --startup-project src/AstroDesk.App/AstroDesk.App.csproj `
  --context AstroDeskDbContext
```

Do not edit an already published migration to change production history. Add a new migration.

## Test strategy

### Automated

Automated tests should cover:

- session lifecycle and validation;
- frame, exposure, integration, and remaining-time calculations;
- dew point/risk calculations;
- settings serialization;
- SQLite repositories and constraints;
- migrations and database initialization;
- ADB device/status parsing;
- executable lookup and sensitive-value redaction;
- scrcpy argument generation and lifecycle orchestration;
- input message translation and ADB fallback arguments;
- letterboxing, DPI, rotation, and reverse coordinate mapping;
- histogram calculations and throttling behavior; and
- preview zoom geometry.

Use fakes for child processes, window handles, providers, clocks, and dispatchers. Tests should not require a connected personal phone.

### Windows integration

Some boundaries cannot be proven by unit tests:

- `PrintWindow`/`BitBlt` output from the actual scrcpy SDL window;
- off-screen, non-minimized capture across display drivers;
- Win32 message forwarding accepted by the installed scrcpy build;
- keyboard focus and clipboard behavior;
- per-monitor DPI changes;
- sleep/wake recovery; and
- device-specific ADB properties.

Changes to these areas require a manual test record. Use the hardware-validation issue template and do not convert a test pass on one machine into a universal support claim.

## S23 Ultra hardware-validation checklist

Record Windows build, laptop/GPU/display configuration, phone model, Android/One UI version, ADB version, scrcpy version, cable/port, and AstroDesk commit. Do not publish the raw ADB serial.

Validate:

- authorization, reconnect, disconnect, and multiple-device selection;
- model, Android, battery, charging, battery temperature, voltage, storage, resolution, and orientation display;
- portrait and landscape capture;
- fit and pixel-perfect sizing;
- resize and per-monitor DPI at 100%, 125%, 150%, and 200% where available;
- left click, drag, long press, wheel, right click, typing, paste, escape, arrows, Back, Home, Recents, volume, and power;
- capture restart after scrcpy crash;
- laptop sleep/wake;
- preview FPS and memory stability during a long session;
- manual frame count, pause/resume/end, notes, and history persistence;
- preview screenshots, histogram throttling, overlays, zoom, freeze, and red mode; and
- experimental shutter control only after explicit opt-in.

File separate issues for failures that differ by scrcpy version, GPU, DPI, rotation, or phone software.

## Logging and diagnostics

Rolling logs default to `%LOCALAPPDATA%\AstroDesk\Logs`, retain a bounded number of files, and roll by date/size. Logging must never terminate the application.

Diagnostic reports may include:

- AstroDesk version/commit;
- Windows build and display scaling;
- ADB and scrcpy versions;
- phone model and Android version;
- scrubbed log excerpts;
- capture frame/client sizes and rotation; and
- exact reproduction steps.

The rolling file logger retains Information and higher. External-tool standard error is logged at Warning; standard output is emitted at Debug and is not retained by the default file logger unless logging configuration is changed.

Remove device serials, private coordinates, names, notes, paths, screenshots, and other personal data before publishing logs. The process layer redacts selected serials, but contributors remain responsible for reviewing attachments.

## Publish and package

Create a self-contained x64 publish:

```powershell
dotnet publish src/AstroDesk.App/AstroDesk.App.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output artifacts/publish/win-x64
```

Smoke-test the output on a clean Windows 11 x64 machine:

```powershell
.\artifacts\publish\win-x64\AstroDesk.exe
```

The current release format is a portable ZIP. A release must contain:

- the complete publish output;
- `README.md`;
- `LICENSE`;
- `CHANGELOG.md`; and
- a SHA-256 checksum file.

The repository does not contain an installer or signing credentials. If an MSI/MSIX/WiX installer is added later:

- keep installer source reproducible;
- install per user unless elevation is genuinely required;
- do not bundle ADB/scrcpy without checking their licenses and update model;
- do not write tool paths or private data into machine-wide locations;
- support clean uninstall without deleting user session data by default; and
- obtain signing material through the release environment, never through Git.

## GitHub Actions

`.github/workflows/ci.yml` restores, verifies formatting, builds, and tests on Windows.

`.github/workflows/release.yml` runs tests, publishes `win-x64`, creates a portable ZIP and checksum, uploads the artifact, and creates or updates a GitHub release only for a `v*` tag.

Before tagging:

1. update `CHANGELOG.md`;
2. set the `Version` in `src/AstroDesk.App/AstroDesk.App.csproj`;
3. run formatting, Release build, and all tests;
4. complete the hardware-validation gate appropriate to the change;
5. commit and push;
6. create a matching tag such as `v0.1.0`; and
7. verify the ZIP and checksum on a clean Windows 11 x64 machine.

The workflow uses the repository-scoped GitHub token supplied by Actions. No personal token or credential belongs in the repository.

## Coding expectations

- Keep nullable warnings clean.
- Prefer records for immutable messages/value objects and explicit ownership for disposable resources.
- Use asynchronous I/O and cancellation for external processes, providers, and storage.
- Keep capture/image processing off the WPF UI thread.
- Bound queues and document stale-work behavior.
- Avoid broad exception swallowing; catch at a boundary, log safely, and return actionable state.
- Keep Win32 interop small and test the surrounding translation/geometry.
- Preserve `Unavailable` rather than inventing data.
- Do not add AI, accounts, social features, analytics, or telemetry.
- Link intentional TODOs to [ROADMAP.md](ROADMAP.md) or an entry in [.github/ISSUES_TO_CREATE.md](.github/ISSUES_TO_CREATE.md).
