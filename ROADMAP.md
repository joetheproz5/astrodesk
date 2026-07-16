# AstroDesk roadmap

AstroDesk is focused on one workflow: operating a tripod-mounted Android phone from a Windows laptop while recording an astrophotography session. Roadmap priority is determined by field reliability, dark-site usability, data integrity, and honest device behavior—not by feature count.

Issue-ready entries are in [.github/ISSUES_TO_CREATE.md](.github/ISSUES_TO_CREATE.md) because GitHub CLI and an authenticated repository are not available in the current development environment.

## Release principles

- scrcpy remains the mirroring/control engine.
- Live preview never uses repeated ADB screenshots.
- Session state remains useful when the phone, scrcpy, capture, weather, or internet fails.
- Missing values display as `Unavailable`; no fake phone, weather, astronomy, or location data.
- Experimental device actions are disabled by default and clearly labeled.
- No AI, account, social, analytics, advertising, or telemetry features.
- A feature is not called hardware-supported until it has a recorded physical-device test.

## 0.1 — First field-test release

The first release is a Windows 11 x64, portable, pre-1.0 build.

### Product scope

- dark shooting workspace centered on the phone preview;
- ADB connection state, authorization guidance, device selection, reconnect, and phone status;
- managed scrcpy launch with a unique hidden/off-screen window;
- embedded preview through the current Win32 capture abstraction;
- DPI/letterbox/rotation-aware input mapping;
- direct mouse/keyboard forwarding with limited ADB fallback;
- start, pause, resume, and end session lifecycle;
- manual frame counter and exposure timer;
- notes, SQLite history, filters, and portable exports;
- rule-of-thirds/crosshair and additional non-phone overlays;
- 2x/4x/8x inspection zoom, focus magnifier, and freeze frame;
- throttled luminance/RGB histogram and clipping percentages;
- PNG preview screenshots;
- normal dark, dim, and full red display modes;
- saved/manual/searched locations and Lebanon seed locations;
- no-key weather plus local moon/twilight calculations;
- local rolling logs and failure-oriented error messages; and
- automated unit/integration coverage for testable logic.

### Release gates

- Release build and all automated tests pass from a clean checkout.
- Formatting verification passes.
- EF Core reports no uncommitted model changes.
- Portable `win-x64` publish starts on a clean Windows 11 machine.
- S23 Ultra/ThinkPad hardware validation is recorded.
- Portrait/landscape, common DPI settings, resize, fullscreen, and multi-monitor mapping are checked.
- A two-hour preview/session soak does not show unbounded memory growth.
- USB disconnect/reconnect, scrcpy crash, and sleep/wake behavior are tested.
- Experimental shutter control remains off by default and carries an in-product warning.
- README screenshots are replaced only after the represented behavior is verified.

### Known 0.1 constraints

- Current capture is `PrintWindow` with `BitBlt` fallback, not Windows Graphics Capture.
- Hardware integration has not been validated in this repository's current environment.
- The frame counter is manual.
- ADB shutter-coordinate control is experimental.
- Current-device location is unavailable.
- The package is portable and unsigned; no installer or SmartScreen reputation.

## 0.2 — Capture and input resilience

Planned work:

- add a Windows Graphics Capture backend behind `IWindowCaptureService`, retaining Win32 fallback;
- probe scrcpy version/capabilities before enabling optional toolbar actions;
- expand multi-monitor, non-uniform DPI, rotation-race, and stale-context tests;
- improve sleep/wake and display-topology recovery;
- add capture/backend diagnostics that do not expose sensitive data;
- run GPU/display-driver and scrcpy-version compatibility tests; and
- establish a performance budget for FPS, CPU, memory, histogram cadence, and screenshot latency.

## 0.3 — Session and field-workflow polish

Planned work:

- refine history sorting, duplicate-as-plan, deletion recovery, and export UX;
- add Windows current-location support with explicit permission and a manual fallback;
- improve observing-location import/export;
- add equipment/overlay preset management;
- improve keyboard-only navigation, high-contrast behavior, and screen-reader labels;
- add localization readiness while keeping astronomical units explicit; and
- refine unattended recovery prompts without enabling unsafe automatic device actions.

## Later-compatible work

These are considered only when they preserve the core design:

- wireless ADB pairing/selection as an optional alternative to USB;
- reliable, opt-in capture-event integration if Samsung/Android exposes a supported signal;
- signed installer/update delivery with reproducible packaging;
- richer session export/import while keeping SQLite local and authoritative;
- configurable capture backend selection and diagnostics; and
- an extension interface for non-Samsung Android camera workflows without turning AstroDesk into a generic dashboard.

Automatic shot detection must never be advertised based on unreliable heuristics. OCR is not an acceptable normal-operation dependency.

## Explicit non-goals

- AI-assisted framing, focus, processing, or recommendations
- user accounts or cloud synchronization
- social sharing or community feeds
- telemetry, analytics, advertising, or tracking
- photo stacking or full astrophotography post-processing
- replacing Samsung Camera
- rebuilding the scrcpy protocol
- using ADB screenshots as the live-preview transport
- silently generating weather, astronomy, phone, or location values
- remotely controlling a phone without clear local user authorization

## Prioritization

Use this order when issues compete:

1. prevent session-data loss;
2. prevent unsafe or misdirected phone input;
3. restore preview/device connectivity;
4. maintain dark-site usability;
5. reduce CPU, memory, and latency;
6. improve diagnostics and test coverage; and
7. add workflow convenience.

Every roadmap issue should include reproducible acceptance criteria and identify whether it can be automated, requires Windows integration testing, or requires the target phone/laptop.
