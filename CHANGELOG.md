# Changelog

All notable changes to AstroDesk are documented here.

The project follows [Semantic Versioning](https://semver.org/) while pre-1.0 releases may contain intentional compatibility changes. Dates use `YYYY-MM-DD`.

## [Unreleased]

### Planned

- Physical S23 Ultra and ThinkPad hardware-validation results.
- Compatibility findings across scrcpy versions, GPUs, display scaling, rotation, disconnect/reconnect, and sleep/wake.
- Replacement of README screenshot placeholders with validated application captures.
- A Windows Graphics Capture backend with Win32 fallback.
- Signed installer packaging after a reproducible release process is established.

## [0.1.10] - 2026-07-17

### Added

- Added native Windows touch handling on the embedded phone preview, with reliable ADB tap and swipe forwarding over USB or wireless connections.
- Added landscape-aware touch coordinate scaling for the S23 Ultra's 3088 × 1440 rotated display.

### Fixed

- Reset the shooting preview to the full uncropped phone frame whenever scrcpy starts.
- Prevented one-finger phone interaction from being interpreted as workspace scrolling.
- Prevented pointer presses inside AstroDesk from activating or exposing the hidden external scrcpy window.

### Changed

- Removed the 2× zoom button from the main shooting workspace.
- Made the live phone preview slightly taller and wider while preserving its exact 3088 × 1440 landscape ratio and keeping live conditions visible.

## [0.1.9] - 2026-07-17

### Added

- Added genuine Android Wireless ADB QR pairing with a generated one-time secret, exact mDNS service matching, automatic endpoint discovery, and a two-minute cancellable scan window.
- Added automatic discovery of the phone's current pairing-code address so an expired temporary port is replaced before pairing.

### Fixed

- Replaced ADB's opaque `protocol fault` failure with guidance that explains when the phone's temporary pairing screen or network path is no longer reachable.
- Stopped ordinary touch buttons from retaining a hover/focus border after release; stateful toggles remain highlighted only while enabled.
- Kept manual code pairing available when mDNS discovery itself is unavailable.

### Changed

- QR mode temporarily hides unrelated wireless controls so the code stays large, centered, and visible without scrolling.

## [0.1.8] - 2026-07-17

### Added

- Added a one-tap USB-to-wireless ADB switch that discovers the phone's Wi-Fi address, enables TCP/IP mode, connects, and selects the wireless device automatically.
- Added direct Wireless ADB pairing, connect, and disconnect controls with pairing-code redaction and clear Android setup guidance.

### Changed

- Rebuilt the Shoot page as one clean, touch-scrollable surface without the permanent sidebar or duplicate tool rows.
- Gave Weather Now a wide five-metric layout and kept Moon illumination, light pollution, altitude, dew point, and dew risk in the essential conditions strip.
- Moved Grid, Crosshair, Focus, zoom, clear, and fullscreen controls into a narrow rail beside the landscape phone preview.
- Replaced the blue-heavy theme with a neutral graphite and teal field palette and removed duplicate device status pills.
- Reduced the minimum window size to 820 x 500 while retaining touch scrolling for smaller laptop viewports.
- Excluded the embedded phone surface from workspace scrolling so Android touch input and page swipes do not conflict.

## [0.1.7] - 2026-07-17

### Changed

- Made the embedded scrcpy phone display the visual focus of the shooting workspace and locked its landscape frame to the Galaxy S23 Ultra's 3088 x 1440 display ratio.
- Added a visible Rotate phone control so the mirrored Android display can be aligned with the landscape workspace in one tap.
- Replaced the ambiguous toolbar panel buttons with a permanently visible, clearly labeled sidebar control rail.
- Compressed the pinned location, recommendation, weather, Moon, and light-pollution information into a single shallow strip to give the phone substantially more vertical space.
- Floated preview status and framing controls beside the phone so they no longer consume rows above or below it.
- Wrapped and trimmed empty-state and error text inside narrow phone previews instead of allowing it to overflow the device frame.
- Verified the redesigned phone-first workspace and both sidebar controls at the minimum 1050 x 700 window size with Windows display scaling.

## [0.1.6] - 2026-07-17

### Changed

- Added explicit one-finger drag scrolling with a movement threshold and inertial glide across AstroDesk's scrollable panels, including gestures that begin over nested controls.
- Expanded the embedded scrcpy preview to the full shooting-workspace width by default.
- Moved the detailed Night Brief and Session workspace into mutually exclusive, touch-friendly slide-out panels opened from menu buttons above the preview.
- Kept the full live location, shooting recommendation, weather, Moon, and light-pollution rail pinned above the enlarged preview.
- Verified the preview-first layout and both slide-out panels at the minimum 1050 x 700 window size with Windows display scaling.

## [0.1.5] - 2026-07-17

### Changed

- Added a pinned conditions rail above the shooting workspace so the current location, altitude, automatic time zone, shooting score, recommendation, and best window remain visible without scrolling.
- Kept live temperature, cloud cover, visibility, rain chance, humidity, wind, dew point, and dew risk together in the always-visible weather card.
- Kept Moon phase, illumination, current position, light-pollution zone, and dark-sky window visible beside the weather card.
- Verified the layout at AstroDesk's minimum 1050 x 700 window size and common Windows display scaling.

## [0.1.4] - 2026-07-16

### Changed

- Tightened the preview and framing toolbars to five high-value controls each, moving the remaining actions into their existing touch-friendly overflow menus.
- Reduced center-column minimum width so the Night Brief, phone preview, and Session workspace remain balanced at common Windows display-scaling settings.
- Re-ran the visual audit against the physical rendered window after the toolbar adjustment.

## [0.1.3] - 2026-07-16

### Changed

- Rebuilt the shooting dashboard around the embedded phone preview with wider, calmer side panels and substantially more breathing room.
- Replaced the orange-brown palette with a moonlit blue/slate visual system, softer surfaces, larger typography, rounded controls, and clearer selected/navigation states.
- Reorganized conditions into a concise Night Brief with location, tonight's verdict, weather tiles, sky quality, darkness, and Moon cards.
- Split the shooting workflow into focused Plan, Progress, and Timer tabs instead of one long crowded form.
- Moved secondary phone and framing actions into touch-friendly overflow menus while keeping the most-used controls immediately available.
- Redesigned History with a clear page header, primary filter row, collapsible advanced filters, and a better-balanced list/detail layout.
- Refreshed Settings with stronger page hierarchy, wider spacing, and updated online-provider privacy language.
- Preserved touchscreen panning, inertia, full red mode, keyboard shortcuts, and every existing shooting control.

## [0.1.2] - 2026-07-16

### Added

- A target-aware tonight score and best shooting window based on the hourly forecast, darkness, Moon position, skyglow, wind, humidity, dew spread, precipitation risk, and visibility.
- Open-Meteo elevation plus 36 hours of hourly weather data for the detected or selected coordinate.
- Current Moon altitude, compass direction, azimuth, and a 36-hour Moon track calculated locally.
- Objective 2024 zenith-brightness estimates from the public David Lorenz Light Pollution Atlas, including atlas zone, artificial-to-natural brightness ratio, and estimated mag/arcsec².
- Tests for recommendation quality, target-specific weighting, hourly weather parsing, Moon tracking, and compressed light-pollution tile decoding.

### Changed

- Astronomy and recommended-window times now render in the selected coordinate's resolved time zone instead of assuming the laptop's current zone.
- The conditions panel now leads with altitude and a concise tonight verdict while retaining the full weather, darkness, and Moon detail.
- Light-pollution results are deliberately not labeled as a Bortle class because Bortle assessment is subjective and considers the whole visible sky.

## [0.1.1] - 2026-07-16

### Added

- Automatic current-device location through the Windows location service with an in-app shortcut to Windows Location settings.
- Clearly labeled coarse public-IP location fallback through BigDataCloud when Windows Location is unavailable.
- Automatic Open-Meteo time-zone resolution for the detected or selected coordinates.
- Touchscreen panning with inertia for dashboard side panels, settings, history details, lists, drop-downs, and horizontal toolbars.
- Provider tests covering live weather values, visibility conversion, observation offsets, and time-zone metadata.

### Changed

- Live weather and automatic location are enabled by default for new installations; Windows still controls location permission and the feature can be disabled in Settings.
- The conditions panel now leads with the laptop's current location, coordinates, and time zone while keeping search and manual coordinates as fallbacks.
- Weather update text reports the provider observation time instead of only the laptop refresh time.

## [0.1.0] - 2026-07-16

### Added

- .NET 8 WPF solution with Core, Device, Capture, Data, Infrastructure, and test projects.
- Dark-theme application controls for embedded preview, overlays, histogram, zoom/freeze inspection, and night-mode presentation.
- Session lifecycle, exposure/integration calculations, manual frame tracking, notes, status snapshots, search criteria, and portable exports.
- EF Core SQLite model, relationships, constraints, repositories, initial migration, integrity checks, and recovery-oriented initialization.
- ADB executable discovery, device-state parsing, authorization guidance, status retrieval, reconnect monitoring, wireless-compatible service methods, and low-frequency input fallbacks.
- Preferred-device selection for multiple authorized ADB devices.
- scrcpy executable discovery, validated arguments, unique window titles, child-process logging, exact PID/window matching, off-screen non-minimized presentation, state changes, reconnect, and crash handling.
- Win32 embedded-capture abstraction using `PrintWindow` with `BitBlt` fallback, pooled frames, and a bounded latest-frame queue.
- DPI-, letterbox-, client-size-, sizing-mode-, and rotation-aware coordinate mapping with unit tests.
- Zoom-view input mapping and ADB fallback scaling to the reported physical phone resolution.
- Win32 pointer/key/text/clipboard translation and scrcpy shortcut mapping.
- OpenCV luminance/RGB histograms, clipping percentages, throttling, and PNG preview screenshots.
- Open-Meteo weather/location providers requiring no API key.
- Local Astronomy Engine moon and twilight calculations.
- Lebanon example locations without geographic restriction.
- Local application paths, session folders, JSON/Markdown exports, and rolling logs.
- Collision-resistant session folder names containing a short session ID.
- Opt-in Open-Meteo weather/geocoding controls that are disabled by default.
- Windows CI, portable `win-x64` release automation, templates, Dependabot configuration, and project documentation.

### Security and privacy

- No AI, user accounts, analytics, advertising, or telemetry.
- Device serial redaction at external-process logging boundaries.
- No committed provider credentials or signing material.
- Online provider failure returns unavailable data rather than fabricated observations.

### Known limitations

- The physical target setup was unavailable for end-to-end validation in this development environment.
- The embedded capture backend is `PrintWindow`/`BitBlt`, not Windows Graphics Capture.
- Off-screen capture and direct Win32 input may vary by scrcpy, SDL, GPU, display driver, DPI, and multi-monitor configuration.
- Redirected scrcpy standard output is Debug-level and is not retained by the default Info-level rolling file logger.
- Frame counting is manual; automatic Samsung Camera capture detection is not claimed.
- ADB shutter-coordinate control is experimental and disabled by default.
- Current-device location is unavailable.
- Saved-location create/edit/delete management is incomplete.
- Requested-date astronomy needs a consistent instant for moon values as well as twilight values.
- Preview fullscreen is an in-window mode, and strict red-only output still needs a complete visual audit.
- History is capped at 500 records without pagination or complete session editing.
- Preview screenshots are not full-resolution phone photographs.
- Release packaging is portable, unsigned, and x64-only; no installer is included.
