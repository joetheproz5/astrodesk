# scrcpy integration

AstroDesk uses scrcpy as the real Android mirroring and control engine. It does not implement the scrcpy protocol, decode a separate video stream, or use repeated ADB screenshots for live preview.

The integration has four responsibilities:

1. find and start the correct `scrcpy.exe`;
2. identify and preserve the real scrcpy window;
3. capture that window into the AstroDesk preview; and
4. forward mapped input back to the real window, with limited ADB fallback.

## Executable discovery

`ExecutableLocator` looks for scrcpy in this order:

1. `ScrcpyLaunchOptions.ExecutablePath`;
2. the configured `DeviceToolOptions.ScrcpyExecutablePath`;
3. `ASTRODESK_SCRCPY_PATH`;
4. the application base directory;
5. the current directory;
6. any explicitly supplied search directories; and
7. the Windows `PATH`.

The configured value may point to the executable or to a directory. No developer-machine path is compiled into AstroDesk.

ADB uses the same locator strategy with `DeviceToolOptions.AdbExecutablePath` and `ASTRODESK_ADB_PATH`.

## Launch contract

Each launch generates a title in this form:

```text
AstroDesk-Phone-Preview-<32-character GUID>
```

The arguments are constructed as separate process arguments rather than a shell command. A typical launch is equivalent to:

```powershell
scrcpy `
  --window-title "AstroDesk-Phone-Preview-..." `
  --no-audio `
  --video-bit-rate=12M `
  --max-size=1440 `
  --max-fps=60 `
  --stay-awake `
  --serial "<selected-device>"
```

The following are configurable:

- executable path;
- selected ADB serial;
- video bitrate, validated between 1 and 100 Mbps;
- maximum dimension, validated between 1 and 16,384 pixels;
- maximum FPS, validated between 1 and 240;
- stay-awake behavior;
- phone-screen-off-on-launch behavior;
- title prefix; and
- window-discovery timeout.

Audio is always disabled for the AstroDesk preview. `--turn-screen-off` is added only when explicitly requested. Unsupported options must be disabled or reported clearly instead of retried blindly.

scrcpy starts as a child process with standard output and standard error redirected. The selected serial is marked as sensitive and redacted before output events or logging. Standard error is written at Warning level and is retained by the default rolling file logger. Standard output is emitted at Debug level; the default Info-level rolling file logger does not retain every stdout line.

## Finding and hiding the window

AstroDesk searches for the exact generated title and verifies that the window belongs to the started child process ID. This avoids attaching to an unrelated scrcpy instance.

After discovery, `Win32ScrcpyWindowManager`:

- records the original position and extended style;
- removes `WS_EX_APPWINDOW`;
- adds `WS_EX_TOOLWINDOW`;
- shows the window without activation; and
- moves it to `-32000, -32000` without minimizing it.

The window is kept alive because it remains scrcpy's rendering and input target. Minimizing is deliberately avoided: some capture methods stop receiving useful content from a minimized window.

On a normal stop, partial startup failure, or detected crash, AstroDesk attempts to restore the original style and bounds before disposing the child process. Restoration is best-effort because the window may already have been destroyed.

## Current embedded-capture backend

The current implementation is:

```text
IWindowCaptureService
└── Win32WindowCaptureService
    ├── PrintWindow(client area, full-content flag)
    └── BitBlt fallback
```

This is a suitable Win32 capture backend abstraction. It is **not Windows Graphics Capture**, and the product must not claim otherwise.

For every capture:

1. query the scrcpy client rectangle;
2. allocate compatible GDI surfaces;
3. ask `PrintWindow` to render the client area;
4. use `BitBlt` if `PrintWindow` fails;
5. copy BGRA pixels into a pooled buffer;
6. publish a disposable `CaptureFrame`; and
7. release all GDI objects and device contexts.

The capture loop runs away from the UI thread. Its bounded channel has capacity two. If delivery falls behind, an older frame is disposed so the preview trends toward the newest available image rather than accumulating latency.

Consumers receive a borrowed frame during the event callback. A consumer that needs the pixels afterward must copy them. Histogram processing and saved screenshots follow this rule.

### Why an abstraction is retained

`IWindowCaptureService` allows a future Windows Graphics Capture implementation to replace or coexist with the current backend without changing:

- WPF rendering;
- coordinate mapping;
- histogram processing;
- preview screenshots;
- freeze inspection;
- error presentation; or
- scrcpy process management.

A WGC backend should be added only with a fallback and a hardware/display-driver test matrix. See [.github/ISSUES_TO_CREATE.md](.github/ISSUES_TO_CREATE.md).

## Preview presentation

The WPF preview:

- preserves the captured frame's aspect ratio;
- supports fit and pixel-perfect sizing;
- letterboxes instead of stretching;
- updates after resize and rotation state changes;
- can display connection/FPS and capture errors;
- supports non-phone overlays, zoom, freeze inspection, and histogram UI; and
- can apply a laptop-only red tint; and
- provides an in-window distraction-free preview layout rather than a verified borderless/exclusive fullscreen mode.

Overlays and red tint modify only AstroDesk's rendered/captured preview pixels. They do not modify the phone display or Samsung Camera output.

Preview screenshots are encoded as PNG from the most recent embedded frame. They are not equivalent to the full-resolution photograph stored by Samsung Camera.

## Input delivery

The direct path posts translated Win32 messages to the hidden scrcpy window:

- left/right/middle button down and up;
- pointer movement while dragging;
- mouse wheel;
- key down/up;
- Unicode character input;
- scrcpy shortcut chords for Back, Home, Recents, volume, power, rotation, paste, and screen-off where supported.

Clicking the preview focuses the WPF control, and a direct left-button-down asks Windows to foreground the real scrcpy target before subsequent keyboard messages are sent.

For supported failures, `InputRouter` can fall back to:

```powershell
adb -s <serial> shell input tap X Y
adb -s <serial> shell input swipe X1 Y1 X2 Y2 DURATION_MS
adb -s <serial> shell input keyevent KEYCODE
adb -s <serial> shell input text TEXT
```

ADB fallback:

- requires an authorized selected device;
- is used only after direct delivery fails;
- is slower and should not be the normal drag path;
- scales scrcpy client coordinates to the phone's reported physical screen resolution when that status value is available;
- does not provide an equivalent fallback for every scrcpy shortcut; and
- must never log the raw serial.

Mouse wheel and right/middle-button semantics depend on scrcpy/Android support and currently have no general ADB-equivalent fallback.

See [INPUT_MAPPING.md](INPUT_MAPPING.md) for geometry and key details.

## Lifecycle states

`ScrcpyService` exposes:

- `Stopped`
- `Starting`
- `Running`
- `Reconnecting`
- `Stopping`
- `Crashed`
- `Faulted`

Only one managed session is active at a time. A repeated start returns the current healthy session. Reconnect reuses the last validated launch options.

If scrcpy exits unexpectedly:

- the session/window references are cleared;
- the window restoration is attempted;
- the child process is disposed;
- state changes to `Crashed`;
- the exit code is logged; and
- the UI can keep the shooting session alive while offering reconnect.

If startup cannot find the exact window before the timeout, the partial child process is stopped and a `ScrcpyWindowNotFoundException` includes the title and timeout.

## Version and capability handling

AstroDesk currently builds arguments around commonly supported modern scrcpy flags. It does not yet maintain a complete version/capability negotiation table.

Until capability detection is implemented:

- require a recent stable scrcpy;
- show the installed version in diagnostics where possible;
- disable screen-off control unless support has been confirmed;
- fail with the captured scrcpy diagnostic when an option is unknown; and
- do not silently remove requested safety/selection flags.

The following command is the first compatibility check:

```powershell
scrcpy --version
scrcpy --help
```

## Failure diagnosis

### Executable not found

Confirm the configured path, environment variable, or `PATH`. AstroDesk's error includes the searched locations. Do not solve this by adding a machine-specific path to source.

### Window not found

- Run scrcpy manually with a title to confirm that it creates a normal top-level window.
- Check for immediate scrcpy failure in redirected output.
- Confirm the selected device is authorized.
- Close orphaned scrcpy processes and retry.

### Black or stale capture

- Confirm the window is alive and not minimized.
- Try the primary monitor and common DPI settings.
- Check GPU/display-driver differences.
- Test `PrintWindow` and `BitBlt` behavior before changing mapping logic.
- Record the scrcpy version and SDL renderer information if available.

An inability to capture an off-screen window through this backend is a capture limitation, not proof that ADB screenshots should replace the live path.

### Input reaches the wrong place

- Verify frame size, client size, DPI scale, rotation, and letterbox rectangle.
- Verify the current zoom source rectangle and test both 1x and inspection zoom.
- Use the debug overlay.
- Run the mapping unit tests.
- See [INPUT_MAPPING.md](INPUT_MAPPING.md).

### scrcpy crashes

Review the redirected output and local log. Check:

- device disconnect/authorization changes;
- unsupported arguments;
- another process using the device;
- USB instability;
- scrcpy/ADB version mismatch; and
- display/GPU errors.

## Hardware validation gate

Embedded control is not considered fully validated until all of these pass on the target setup:

- S23 Ultra portrait and landscape;
- Samsung Camera photo/pro/manual UI;
- click, drag, long press, wheel, right click, typing, paste, and toolbar shortcuts;
- 100%, 125%, 150%, and 200% Windows scaling where available;
- window resize, fullscreen, and monitor changes;
- phone rotation while preview is running;
- USB disconnect/reconnect;
- scrcpy forced crash and restart;
- laptop sleep/wake;
- at least a two-hour capture/memory soak; and
- no visible normal-use scrcpy taskbar window.

Record failures by scrcpy version, Android/One UI version, Windows build, GPU, display topology, and scaling. Never include a raw device serial.
