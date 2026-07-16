# Input mapping

AstroDesk displays a captured image of a separate scrcpy client window. A pointer coordinate from WPF cannot be forwarded unchanged: the preview may be letterboxed, DPI-scaled, resized, rotated, or rendered at a different size from the real scrcpy client.

`AstroDesk.Capture.Geometry.CoordinateMapper` owns this conversion. The mapping code is pure geometry and is covered by unit tests independently of WPF, Win32, scrcpy, and ADB.

## Coordinate spaces

| Space                      | Units                               | Example                                                    |
| -------------------------- | ----------------------------------- | ---------------------------------------------------------- |
| Embedded control           | WPF device-independent pixels (DIP) | `Point(640, 360)` inside the preview control               |
| Physical container         | monitor pixels                      | embedded DIP multiplied by `DpiScaleX/Y`                   |
| Rendered preview rectangle | DIP                                 | the aspect-ratio-preserving image area after letterboxing  |
| Captured frame             | pixels                              | BGRA frame dimensions returned by the capture backend      |
| scrcpy client              | pixels                              | actual client-area coordinates accepted by window messages |
| Normalized                 | 0 to 1                              | orientation-independent intermediate coordinate            |

The mapping context contains:

```text
EmbeddedSizeDip
CapturedFrameSizePixels
ScrcpyClientSizePixels
DpiScaleX / DpiScaleY
Rotation
SizingMode (Fit or PixelPerfect)
```

All sizes and DPI scales must be positive.

## Rendered rectangle

For fit mode, let:

```text
containerWidthPx  = embeddedWidthDip  × dpiScaleX
containerHeightPx = embeddedHeightDip × dpiScaleY

scale = min(
    containerWidthPx  / frameWidthPx,
    containerHeightPx / frameHeightPx)

renderedWidthPx  = frameWidthPx  × scale
renderedHeightPx = frameHeightPx × scale
```

The image is centered:

```text
leftPx = (containerWidthPx  - renderedWidthPx)  / 2
topPx  = (containerHeightPx - renderedHeightPx) / 2
```

The returned rectangle is converted back to DIP for WPF rendering and hit testing.

Pixel-perfect mode uses the same calculation but clamps `scale` to at most `1`. A frame is never enlarged beyond one source pixel per physical display pixel. It may still be reduced when the control is smaller than the frame.

## Letterbox handling

A point outside the rendered rectangle is not part of the phone image. `MapToScrcpy` returns `IsInsidePreview = false`, and pointer input must be ignored.

This prevents clicks in a black side/top bar from being clamped to a phone edge and accidentally activating a Camera control.

Keyboard and text events do not require a current pointer inside the preview, but the preview must already own keyboard focus and a scrcpy session must be available.

## Normalized mapping

For a point inside the rendered rectangle:

```text
displayedU = (pointDip.X - rendered.Left) / rendered.Width
displayedV = (pointDip.Y - rendered.Top)  / rendered.Height
```

Both values are clamped to `[0, 1]`.

Captured-frame coordinates are:

```text
capturedX = displayedU × (capturedWidth  - 1)
capturedY = displayedV × (capturedHeight - 1)
```

scrcpy client coordinates use the unrotated normalized point:

```text
clientX = clientU × (clientWidth  - 1)
clientY = clientV × (clientHeight - 1)
```

The `- 1` keeps normalized `1.0` on the last valid pixel index.

## Rotation

`Rotation` describes how the displayed captured frame is rotated relative to the scrcpy client coordinate system. The displayed `(u, v)` point is unrotated as follows:

| Rotation | `clientU` | `clientV` |
| -------- | --------- | --------- |
| 0°       | `u`       | `v`       |
| 90°      | `v`       | `1 - u`   |
| 180°     | `1 - u`   | `1 - v`   |
| 270°     | `1 - v`   | `u`       |

Reverse mapping applies the opposite transform and is tested as a round trip for all four rotations.

Rotation state must be updated together with the frame/client dimensions. Reusing a pre-rotation context can target the wrong part of the phone.

## DPI behavior

WPF input arrives in DIP while source/client sizes are physical pixels. `VisualTreeHelper.GetDpi` supplies the current per-monitor scale. The application manifest requests Per-Monitor V2 awareness.

Do not:

- multiply a point by DPI after it has already been normalized within the DIP rendered rectangle;
- use the primary-monitor scale for a window on another monitor;
- cache a DPI value across `WM_DPICHANGED`; or
- assume X and Y scales are always identical.

Uniform scaling should not change the normalized result. A unit test verifies the same logical center at 100% and 150%.

## Captured frame size versus client size

The captured frame and scrcpy client usually match, but the mapper does not assume they do. Capture cropping, backend differences, renderer borders, or later transforms may produce different dimensions.

- Captured coordinates are useful for debug overlays, histograms, screenshots, and pixel inspection.
- Client coordinates are the only coordinates sent to the hidden window.

Always query the current client area rather than using the phone's advertised `wm size` as the direct Win32 target.

## Preview zoom

Inspection zoom changes which normalized source region is rendered. `CoordinateMappingContext.SourceViewNormalized` carries that current source rectangle into the mapper.

The required inverse transform for a zoom viewbox is:

```text
sourceU = viewbox.Left + displayedU × viewbox.Width
sourceV = viewbox.Top  + displayedV × viewbox.Height
```

That source point then passes through the normal rotation/client mapping. The mapping suite verifies that a zoomed view center maps to the selected zoom center.

## Pointer event translation

The WPF preview captures the mouse during a press so drags continue when the pointer moves rapidly. The input coordinator tracks current buttons, drag start, and duration.

| WPF interaction | Preferred delivery                       | Fallback                                |
| --------------- | ---------------------------------------- | --------------------------------------- |
| Left click      | `WM_LBUTTONDOWN` / `WM_LBUTTONUP`        | ADB tap if direct delivery failed       |
| Drag            | down, `WM_MOUSEMOVE` with left flag, up  | ADB swipe with measured duration        |
| Long press      | hold direct button-down before button-up | No distinct ADB long-press fallback yet |
| Right click     | right-button messages                    | No general ADB fallback                 |
| Middle click    | middle-button messages                   | No general ADB fallback                 |
| Wheel           | `WM_MOUSEWHEEL` using screen coordinates | No general ADB fallback                 |

Wheel messages use screen rather than client coordinates. The forwarder calls `ClientToScreen` before posting the message.

ADB fallback is intentionally triggered only after the direct sequence reports a failure. ADB coordinates are still derived from the mapped scrcpy client point and require a selected authorized device.

scrcpy client pixels are not guaranteed to equal Android's physical `input` coordinate space when `--max-size` downscales the video. For ADB tap/swipe fallback, AstroDesk scales the mapped client point to the phone's reported physical screen resolution when available. If the status value is unavailable, fallback remains best-effort and must be verified before relying on it for camera controls.

## Keyboard and shortcuts

Clicking the preview gives the WPF control keyboard focus. Direct character input uses `WM_CHAR`; supported non-character keys use key down/up messages. AstroDesk shortcut actions use the scrcpy chords below and selected ADB key events where available.

| AstroDesk action           | scrcpy-window chord                     | ADB fallback          |
| -------------------------- | --------------------------------------- | --------------------- |
| Back / Escape              | `Escape`                                | `KEYCODE_BACK`        |
| Home                       | `Alt+H`                                 | `KEYCODE_HOME`        |
| Recent apps                | `Alt+S`                                 | `KEYCODE_APP_SWITCH`  |
| Volume up                  | `Alt+Up`                                | `KEYCODE_VOLUME_UP`   |
| Volume down                | `Alt+Down`                              | `KEYCODE_VOLUME_DOWN` |
| Power                      | `Alt+P`                                 | `KEYCODE_POWER`       |
| Arrow keys                 | arrow key                               | matching DPAD key     |
| Rotate                     | `Alt+R`                                 | none                  |
| Clipboard paste            | `Alt+V` after setting Windows clipboard | ADB text fallback     |
| Screen off while mirroring | `Alt+O`                                 | none                  |

The exact scrcpy shortcut behavior depends on the installed scrcpy version. Capability-dependent toolbar actions should be disabled when support has not been confirmed.

Text sent through ADB replaces spaces with `%s` and escapes shell metacharacters. Direct character forwarding remains the preferred path.

## Rounding and bounds

The mapper returns double-precision coordinates. The application rounds them to the nearest integer immediately before constructing `MappedPoint`.

Before posting a Win32 message:

- values must remain inside the current client bounds; and
- packed X/Y values must fit Win32's signed 16-bit message-coordinate representation.

Current phone/window sizes are well below that limit. If a future virtual surface can exceed it, the delivery mechanism must change rather than truncating.

## Debug overlay

The debug overlay is disabled by default. A useful report contains:

- embedded cursor DIP;
- rendered preview rectangle;
- mapped captured-frame coordinate;
- mapped scrcpy-client coordinate;
- normalized coordinate;
- captured frame size;
- scrcpy client size;
- DPI X/Y scale;
- rotation;
- sizing mode; and
- inspection zoom/viewbox if active.

Do not include the device serial in screenshots or public logs.

## Test cases

The automated mapping suite covers:

- aspect ratio and centered letterboxing;
- rejection of letterbox clicks;
- differing frame/client sizes;
- normalized equivalence across DPI scaling;
- all four rotation corner mappings;
- forward/reverse round trips; and
- pixel-perfect no-upscale behavior.

Changes to mapping should add boundary cases for:

- first and last client pixel;
- non-square DPI scales;
- odd pixel dimensions and midpoint rounding;
- rapid resize/rotation updates;
- zoom inverse transforms;
- fullscreen and multi-monitor transitions; and
- stale-context rejection.

## Manual validation pattern

Use a harmless screen before testing Samsung Camera controls:

1. enable the debug overlay;
2. tap a visible 3×3 grid of landmarks;
3. compare embedded, captured, and client coordinates;
4. repeat in portrait and landscape;
5. repeat at each available Windows scale;
6. resize and move the app between monitors;
7. test slow and fast drags;
8. test a stationary long press;
9. test wheel, right click, typing, and paste; and
10. only then test Camera buttons.

If the error is systematic, capture the full mapping context before adjusting formulas. Do not add model-specific magic offsets.
