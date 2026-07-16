# Security policy

AstroDesk controls an authorized Android device, launches external executables, captures a local window, stores location/session data, and optionally contacts weather/location providers. Security reports are taken seriously even though the project is pre-1.0.

## Supported versions

Until the first stable release, security fixes are made on the latest release line and the default branch.

| Version                             | Supported |
| ----------------------------------- | --------- |
| Latest pre-release / default branch | Yes       |
| Older pre-releases                  | No        |

Upgrade to the newest available version before reporting a problem that may already be fixed.

## Report a vulnerability

Do not open a public GitHub issue, discussion, or pull request containing vulnerability details.

Use the repository's private **Report a vulnerability** / GitHub Security Advisory flow. Include:

- affected AstroDesk version or commit;
- Windows version;
- ADB and scrcpy versions when relevant;
- impact and realistic attack scenario;
- minimal reproduction steps or proof of concept;
- whether user interaction/USB debugging authorization is required; and
- suggested mitigation, if known.

Remove raw device serials, private coordinates, session notes, screenshots, access tokens, and personal filesystem paths. If private reporting is not enabled yet, contact the repository maintainers through a private channel shown on the repository profile and disclose only enough publicly to request a secure channel.

The project aims to acknowledge a complete report within seven days and provide an initial assessment within fourteen days. Timelines may vary for hardware-specific issues.

## Trust boundaries

### ADB authorization

USB debugging grants significant control over the phone. AstroDesk assumes:

- the user intentionally enabled Developer options and USB debugging;
- the connected computer was authorized on the unlocked phone; and
- the user controls the Windows account running AstroDesk.

Use a trusted laptop and cable. Revoke USB debugging authorizations after using an untrusted computer. Disable USB debugging when it is not needed.

AstroDesk must never bypass Android authorization or silently connect to an unknown device.

### External executables

AstroDesk launches `adb.exe` and `scrcpy.exe` located through configured paths, environment variables, local directories, or `PATH`. A malicious executable in one of those locations can act with the user's privileges.

- Download Platform-Tools and scrcpy from trusted official sources.
- Verify release signatures/checksums when offered.
- Avoid writable untrusted directories early in `PATH`.
- Prefer an explicit path for field machines.
- Do not run AstroDesk elevated unless a separately documented task genuinely requires it.

AstroDesk runs as the current user and does not request administrator privileges in its manifest.

### Direct input and experimental shutter control

Mapped input is sent to the hidden scrcpy window. A geometry, focus, version, or window-selection error can activate the wrong phone control.

- The window must match both the unique title and child process ID.
- Letterbox clicks must be rejected.
- Rotation/DPI/client sizes must be current.
- Experimental shutter-coordinate control must be opt-in and disabled by default.
- Unattended capture must not be recommended before exact-layout validation.

ADB fallback should be limited to the selected device and supported command shapes. User-provided text and endpoints must be passed as process arguments, not interpolated into a shell command.

### Local data

The default data root is `%LOCALAPPDATA%\AstroDesk` and may contain:

- location coordinates;
- target/session metadata;
- notes and problems;
- battery/storage observations;
- preview screenshots;
- logs; and
- the SQLite database.

AstroDesk relies on Windows account and filesystem protections. It does not currently encrypt this data at rest. Protect the Windows account and disk, and avoid placing the data folder in an automatically synchronized location unless that is intentional.

To remove local AstroDesk data:

1. close AstroDesk;
2. back up any wanted sessions;
3. delete `%LOCALAPPDATA%\AstroDesk`; and
4. separately remove configured ADB/scrcpy tools if desired.

Deleting the application does not automatically delete user session data.

### Network providers

Online conditions and geocoding are disabled by default. When the user enables them, Open-Meteo weather requests contain the selected coordinates and geocoding requests contain search text. These requests are subject to the provider's network and privacy behavior.

AstroDesk does not send:

- screen frames;
- preview screenshots;
- phone photos;
- notes;
- device serials;
- phone status; or
- the session database

to weather/location providers.

Astronomy Engine calculations run locally. When the network is unavailable, online fields should show `Unavailable`.

### Logs

Device serials are considered sensitive. The process layer accepts sensitive values and redacts them from logged commands and output.

Do not assume automated redaction removes every private value. Review logs before sharing. Avoid logging:

- raw serials;
- full private paths;
- precise coordinates unless required and consented;
- note/session contents;
- clipboard contents;
- screenshot pixels; and
- environment variables or configuration dumps.

Rolling logs are bounded by file count and size so an error loop cannot grow storage indefinitely.

## Secrets and credentials

AstroDesk's default providers require no API key. The repository must not contain:

- personal access tokens;
- OAuth secrets;
- API keys;
- private signing keys/certificates;
- certificate passwords;
- private package-feed credentials; or
- real device serials.

GitHub Actions uses the repository-scoped token supplied at runtime. Signing or installer credentials, if added later, must use protected CI secrets and an auditable release environment.

If a credential is committed, revoke/rotate it immediately. Removing it from the latest commit is not sufficient because Git history and forks may retain it.

## Release integrity

Current portable releases are unsigned. Users may receive Windows SmartScreen warnings and must verify that the archive came from the expected repository.

The release workflow generates:

- a self-contained `win-x64` ZIP; and
- a SHA-256 checksum file.

Future signing must not weaken reproducibility or place keys in the repository.

## Out of scope

These are generally not security vulnerabilities by themselves:

- the user intentionally authorizing their own phone;
- weather/location availability failures;
- lack of support for a specific scrcpy/GPU combination;
- inaccurate optional temperature data when the phone does not expose a value and AstroDesk shows `Unavailable`; or
- issues requiring a malicious replacement `adb.exe`/`scrcpy.exe` that the user explicitly configured, unless AstroDesk bypasses a documented validation boundary.

Input reaching the wrong phone coordinate, leaking private data, executing injected process arguments, selecting the wrong device/window, or unsafe default experimental control may be security-relevant and should be reported privately.
