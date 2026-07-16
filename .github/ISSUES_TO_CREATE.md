# GitHub issue and label seed

GitHub CLI is not installed and no authenticated public repository is available in the current development environment. These issues and labels should be created after the `astrodesk` repository exists. Do not treat the seed IDs below as existing GitHub issue numbers.

## Requested labels

| Label           | Color    | Description                                                         |
| --------------- | -------- | ------------------------------------------------------------------- |
| `bug`           | `d73a4a` | Something is not working as intended                                |
| `enhancement`   | `a2eeef` | A focused product or engineering improvement                        |
| `scrcpy`        | `5319e7` | scrcpy launch, window, capability, or compatibility work            |
| `adb`           | `0e8a16` | Android Debug Bridge discovery, status, reconnect, or commands      |
| `capture`       | `1d76db` | Embedded window capture, frames, screenshots, or performance        |
| `input`         | `fbca04` | Coordinate mapping, pointer, keyboard, clipboard, or device actions |
| `astronomy`     | `7057ff` | Moon, twilight, dark-sky, or local astronomy calculations           |
| `weather`       | `006b75` | Weather or online location provider work                            |
| `ui`            | `c5def5` | WPF workflow, night mode, accessibility, or presentation            |
| `testing`       | `bfdadc` | Automated, Windows integration, hardware, or soak validation        |
| `documentation` | `0075ca` | User, developer, integration, or release documentation              |

Create labels before issues so templates and seeded issues can apply them.

## Release-blocking issues

### ASTRO-001 — Validate embedded preview and control on the target S23 Ultra/ThinkPad setup

Labels: `testing`, `scrcpy`, `adb`, `capture`, `input`

Why: The current environment has no ADB/scrcpy installation or physical target hardware. Unit tests use fakes and cannot prove SDL off-screen capture or accepted Win32 input.

Acceptance criteria:

- Record Windows build, ThinkPad model/GPU/display layout, phone/Android/One UI, ADB, scrcpy, cable/port, and AstroDesk commit without publishing the serial.
- Validate authorization, disconnect/reconnect, multiple-device selection, and status fields.
- Validate portrait/landscape, resize, fullscreen, and 100%/125%/150%/200% scaling where available.
- Validate click, drag, long press, wheel, right click, typing, paste, Back, Home, Recents, volume, and power.
- Force a scrcpy crash, sleep/wake the laptop, and confirm recovery.
- Run a two-hour preview/session soak and record FPS, CPU, and memory behavior.
- File separate issues for distinct failures.

### ASTRO-005 — Validate astronomy calculations against the requested session date

Labels: `bug`, `astronomy`, `testing`

Why: Rise/set/twilight searches use the requested date, while moon phase, illumination, and altitude currently use the current UTC instant. Historical/planned session views may mix dates.

Acceptance criteria:

- Define the representative instant and time-zone behavior for a requested observing date.
- Calculate all snapshot values against that consistent instant/window.
- Add known-location/date tests and boundary tests around midnight/DST.
- Document UTC/local conversion in exports.

### ASTRO-025 — Make multi-key settings persistence atomic and complete validation

Labels: `bug`, `ui`, `testing`

Why: Configured folders are validated before persistence, but the many settings keys are still written sequentially and some numeric/experimental values are not covered by `ValidateSettings`. A mid-save repository failure can leave a partially updated configuration.

Acceptance criteria:

- Validate all numeric ranges, including defaults, maximum resolution, and experimental coordinates.
- Persist the settings set as one logical operation or roll back partial writes.
- Keep folder probing non-destructive and retain the previous active paths when activation fails.
- Add tests for invalid values, unwritable paths, and partial repository failure.

### ASTRO-031 — Correct exposure-timer drift and state reporting

Labels: `bug`, `ui`, `testing`

Why: Countdown phases can incur an extra delay after reaching zero, and commands can display a state transition the service ignored.

Acceptance criteria:

- Avoid a post-zero phase delay and bound cumulative drift.
- Return/observe whether pause and resume transitions were accepted.
- Add fake-time tests for pause/resume, long durations, completion, and cancellation.

### ASTRO-032 — Complete red-only theming

Labels: `bug`, `ui`, `testing`

Why: The application palette, themed controls, and frozen-frame re-tint have been visually or automatically checked. Windows-owned chrome, application dialogs, and live hardware preview behavior still need a strict night-safety audit.

Acceptance criteria:

- Audit all application-controlled colors and remove blue/green output in Full Red mode.
- Theme remaining progress, selection, focus, and AstroDesk-owned dialog visuals.
- Keep contrast/readability acceptable.
- Add a documented visual-validation checklist and sanitized screenshot.

### ASTRO-033 — Keep history current and add pagination

Labels: `enhancement`, `ui`, `testing`

Why: History is capped at 500 records without pagination and can remain stale after active-session mutations.

Acceptance criteria:

- Refresh or incrementally update history after session/frame/note/screenshot changes.
- Add paging or virtualized incremental loading beyond 500 sessions.
- Support complete intended session edits and preview screenshot thumbnails/details.
- Preserve filters and selection across refreshes where possible.
- Add repository/view-model tests for large histories.

### ASTRO-034 — Add application, view-model, and timer tests

Labels: `testing`, `ui`

Why: Core, Device, Capture, Data, exports, and preview re-tinting have automated coverage, but most WPF composition, main view-model workflows, interaction requests, and the exposure timer still lack direct tests.

Acceptance criteria:

- Test initialization/shutdown, commands, failure states, settings, session workflow, conditions, history, and timer behavior with fakes.
- Keep tests independent of a personal phone, network, and live WPF desktop where possible.
- Add a small Windows UI/integration layer only for behavior that cannot be isolated.

### ASTRO-035 — Add bounded scrcpy stdout diagnostics

Labels: `enhancement`, `scrcpy`, `ui`, `documentation`, `testing`

Why: Child stdout is redirected and redacted but emitted at Debug, while the default rolling file logger retains Information and higher. Normal logs therefore do not preserve every scrcpy stdout line.

Acceptance criteria:

- Provide a bounded in-app diagnostic buffer or configurable file-log level for scrcpy output.
- Keep stderr prominent and redact selected serial values.
- Never allow unbounded process-output growth.
- Add copy/export with an explicit privacy warning.
- Test high-volume output, redaction, retention, and shutdown.

## Capture and scrcpy follow-up

### ASTRO-006 — Add a Windows Graphics Capture backend with Win32 fallback

Labels: `enhancement`, `capture`, `testing`

Acceptance criteria:

- Implement behind `IWindowCaptureService`.
- Preserve bounded latest-frame delivery and disposal rules.
- Select/fallback without changing preview consumers.
- Compare black-frame behavior, FPS, CPU, memory, rotation, and sleep/wake.
- Keep README honest about the active backend.

### ASTRO-007 — Detect scrcpy version and optional capabilities

Labels: `enhancement`, `scrcpy`, `testing`

Acceptance criteria:

- Parse `scrcpy --version` safely.
- Detect required flags and optional screen-off/clipboard behavior.
- Disable unsupported controls with a useful reason.
- Include version in scrubbed diagnostics.
- Test supported, old, malformed, missing, and timeout outputs.

### ASTRO-008 — Harden sleep/wake and display-topology recovery

Labels: `bug`, `capture`, `scrcpy`, `input`, `testing`

Acceptance criteria:

- Detect stale window/capture state after resume.
- Refresh client size, frame size, DPI, rotation, and monitor state.
- Avoid forwarding input through a stale context.
- Reconnect without ending the shooting session.
- Add testable state-machine coverage plus hardware results.

### ASTRO-009 — Establish capture/input compatibility matrix

Labels: `testing`, `scrcpy`, `capture`, `input`, `documentation`

Acceptance criteria:

- Test supported Windows 11 builds, common Intel/AMD/NVIDIA configurations, display scaling, and current scrcpy releases.
- Record current backend results separately from future WGC results.
- Publish only scrubbed compatibility statements.
- Define the minimum supported scrcpy version from evidence.

### ASTRO-010 — Add performance and unmanaged-resource soak tests

Labels: `testing`, `capture`

Acceptance criteria:

- Run at least two hours with preview, histogram, red tint, freeze/unfreeze, screenshots, and resize/rotation.
- Track private bytes, managed heap, GDI handles, CPU, dropped frames, and effective FPS.
- Define alert thresholds.
- Verify bounded queues and prompt shutdown.

## Device and workflow follow-up

### ASTRO-011 — Add guarded calibration for experimental ADB shutter taps

Labels: `enhancement`, `adb`, `input`, `ui`, `testing`

Acceptance criteria:

- Keep the feature disabled by default and label it experimental.
- Require explicit device/orientation/layout calibration.
- Display the mapped coordinate before enabling a sequence.
- Invalidate calibration after resolution/orientation/app-layout changes.
- Provide an immediate stop control and never imply guaranteed capture.

### ASTRO-012 — Add wireless ADB pairing and persistent preferred-device support

Labels: `enhancement`, `adb`, `ui`, `testing`

Acceptance criteria:

- Preserve USB as the recommended first-run path.
- Support explicit endpoint connect/disconnect using existing service methods.
- Never store pairing secrets in plain committed configuration.
- Distinguish USB/wireless state and recovery guidance.
- Test multiple devices and endpoint validation.

### ASTRO-013 — Implement opt-in Windows current-location provider

Labels: `enhancement`, `weather`, `ui`, `testing`

Acceptance criteria:

- Request Windows location permission transparently.
- Keep manual coordinates, saved locations, and search available.
- Return `Unavailable` when permission/source is absent.
- Do not run a hidden location background tracker.
- Document provider/privacy behavior.

### ASTRO-014 — Research reliable Samsung Camera capture-event signals

Labels: `enhancement`, `adb`, `testing`

Acceptance criteria:

- Treat this as research, not a promised feature.
- Reject OCR and repeated screenshot polling as the normal mechanism.
- Document Android/One UI version limitations and permissions.
- Keep manual frame counting authoritative unless a verified event exists.
- Never claim every shot is detected from a heuristic.

## Packaging and documentation

### ASTRO-015 — Create a reproducible signed Windows installer

Labels: `enhancement`, `documentation`, `testing`

Acceptance criteria:

- Select MSI/MSIX/WiX packaging with a documented update/uninstall strategy.
- Do not bundle ADB/scrcpy without license/update review.
- Preserve user data on uninstall by default.
- Obtain signing material only through protected release infrastructure.
- Test clean install, upgrade, uninstall, and offline start on Windows 11 x64.

### ASTRO-016 — Replace README placeholders with validated screenshots

Labels: `documentation`, `ui`, `testing`

Acceptance criteria:

- Capture the main workspace, full red mode, and session history after ASTRO-001 passes.
- Remove serials, private coordinates, notes, notifications, and personal phone content.
- Use representative `Unavailable` states honestly.
- Verify red-mode screenshot accurately represents laptop-only tinting.

## UI/settings completeness

### ASTRO-019 — Add named overlay presets and remaining geometry controls

Labels: `enhancement`, `capture`, `ui`, `testing`

Why: Overlay defaults, color, opacity, thickness, and rectangle size are persisted, but the existing `OverlayPreset` entity is not exposed as named preset management and position/circle geometry remain fixed.

Acceptance criteria:

- Allow adjustment of custom rectangle position and circle size/position within the preview.
- Support named presets through the existing entity without affecting the phone image.
- Persist and validate the remaining custom geometry.
- Keep hide-all immediate and add reset/default behavior.
- Add geometry, serialization, and UI-state tests.

### ASTRO-020 — Validate histogram cadence performance

Labels: `enhancement`, `capture`, `ui`, `testing`

Why: Histogram cadence is configurable and persisted, but its supported settings still need explicit performance validation on field hardware.

Acceptance criteria:

- Preserve single-flight background processing and stale-frame dropping.
- Measure CPU, memory, UI latency, and dropped work for each supported cadence.
- Clamp unsafe values and degrade gracefully when the machine cannot keep up.
- Add automated throttling tests plus the hardware results.

### ASTRO-021 — Add saved observing-location management

Labels: `enhancement`, `weather`, `ui`, `testing`

Why: Seeded/database locations and search results can be loaded, but the current WPF workflow does not provide complete save/edit/delete/default management for user locations.

Acceptance criteria:

- Save a search result or manual coordinate as a named location.
- Edit, delete, and choose a default location.
- Handle online-search failure without an unobserved command exception.
- Prevent confusing duplicate names while allowing global coordinates.
- Keep Lebanon seeds optional and non-restrictive.
- Add repository and view-model tests.

### ASTRO-022 — Add user-configurable keyboard shortcuts

Labels: `enhancement`, `input`, `ui`, `testing`, `documentation`

Why: Useful shortcuts exist, but the settings page does not yet provide collision-checked customization.

Acceptance criteria:

- Configure frame increment, hide overlays, night mode, fullscreen, screenshot, and other supported actions.
- Detect duplicate/reserved combinations.
- Persist and restore mappings locally.
- Keep a documented reset-to-default option.
- Add translation/conflict tests and update README.

## Suggested creation order

1. Create all requested labels.
2. Create ASTRO-001 and ASTRO-005 as first-release blockers.
3. Create ASTRO-025, ASTRO-031, and ASTRO-032 for application reliability before a field release.
4. Create ASTRO-006 through ASTRO-010 for capture/input resilience.
5. Create ASTRO-011 through ASTRO-014 for device/workflow follow-up.
6. Create ASTRO-015 and ASTRO-016 for packaging and documentation.
7. Create ASTRO-019 through ASTRO-022 plus ASTRO-033 through ASTRO-035 for UI/settings/test completeness.
8. Replace each seed ID in documentation or source TODOs with the real GitHub issue link as issues are created.
