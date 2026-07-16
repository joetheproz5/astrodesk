# Contributing to AstroDesk

AstroDesk welcomes focused contributions that improve the Windows phone-assisted astrophotography workflow. Reliability at a dark site matters more than adding broad dashboard features.

## Before starting

- Read [README.md](README.md), [ARCHITECTURE.md](ARCHITECTURE.md), and the relevant integration document.
- Check [ROADMAP.md](ROADMAP.md) and [.github/ISSUES_TO_CREATE.md](.github/ISSUES_TO_CREATE.md).
- Open or claim an issue before a large architectural, database, capture, or input change.
- Do not include credentials, device serials, private coordinates, personal session data, or phone screenshots in a proposal.

Small fixes and test improvements may go directly to a pull request when their scope is clear.

## Project boundaries

Contributions must preserve these rules:

- no AI features;
- no accounts, social features, analytics, advertising, or telemetry;
- no fake device, weather, astronomy, or location values;
- no repeated ADB screenshots for live preview;
- no reimplementation of the scrcpy protocol;
- no automatic claim that every Samsung Camera exposure was detected;
- no hardware-support claim without physical-device evidence;
- no experimental input action enabled by default; and
- no committed secrets, signing certificates, tokens, or machine-specific paths.

Proposals outside these boundaries should explain why AstroDesk, rather than a separate project, is the appropriate home.

## Development setup

Follow [DEVELOPMENT.md](DEVELOPMENT.md). The short verification loop is:

```powershell
dotnet restore AstroDesk.sln
dotnet format AstroDesk.sln --no-restore
dotnet build AstroDesk.sln --configuration Release --no-restore
dotnet test AstroDesk.sln --configuration Release --no-build
```

The WPF solution is built and tested on Windows. The repository's `global.json` selects the expected .NET 8 SDK.

## Branches and commits

Use a short branch name such as:

```text
fix/rotation-mapping
feature/wgc-capture-backend
docs/scrcpy-troubleshooting
test/s23-dpi-matrix
```

Keep commits reviewable and purposeful. Separate mechanical formatting from behavioral changes when practical. Do not rewrite unrelated user work in a dirty checkout.

## Code expectations

- Keep strict nullable analysis clean.
- Keep Core independent of WPF, Win32, EF Core, HTTP, ADB, and scrcpy.
- Put platform and external-process behavior behind narrow interfaces.
- Make resource ownership explicit and dispose unmanaged frames, GDI objects, processes, timers, streams, and cancellation sources.
- Use cancellation tokens for background and I/O work.
- Keep capture/image processing off the UI thread.
- Bound producer/consumer queues and define which stale work is dropped.
- Serialize ADB polling to avoid competing command storms.
- Log at integration boundaries without exposing serials or private session content.
- Return `Unavailable` when a value cannot be obtained.
- Prefer testable geometry/calculation/translation code over logic inside WPF event handlers.
- Link intentional TODOs to a roadmap/issue seed entry.

## Tests

Add or update automated tests for all deterministic behavior.

Examples:

- a coordinate change needs letterbox, DPI, rotation, boundary, and reverse-mapping tests;
- a session change needs lifecycle, validation, duration, and repository tests;
- an ADB parser change needs realistic and missing-value samples;
- a scrcpy argument change needs ordering/value/validation tests;
- a migration needs repository tests and a pending-model-change check;
- a histogram change needs numeric and throttling tests; and
- an input change needs direct message and ADB fallback tests.

Do not make automated tests depend on a contributor's personal phone, serial, filesystem, weather, or network.

## Hardware and Windows integration changes

Capture, hidden-window, direct-input, DPI, sleep/wake, and device-status changes require manual evidence in addition to unit tests.

Use `.github/ISSUE_TEMPLATE/hardware-validation.yml` and record:

- AstroDesk commit;
- Windows build;
- GPU/display topology and scaling;
- laptop model when relevant;
- phone model and Android/One UI version;
- ADB and scrcpy versions;
- cable/connection type;
- exact steps and result; and
- scrubbed diagnostics.

Never post the raw ADB serial. A single successful machine is evidence, not a universal compatibility guarantee.

## Database changes

- Add a new EF Core migration; do not rewrite published migrations.
- Review generated SQL/model changes.
- Preserve upgrade paths and user data.
- Verify SQLite constraints and indexes.
- Test migration/application startup against a real temporary database.
- Document backup or recovery implications.

SQLite remains the source of truth. Session-folder exports are portable assets, not a second writable database.

## Documentation changes

Documentation is part of the product. Update it when behavior, requirements, storage paths, privacy, command lines, or limitations change.

Use precise language:

- say `PrintWindow`/`BitBlt` when that is the backend;
- say “preview screenshot,” not “phone photo”;
- say “experimental,” “unavailable,” or “not hardware-validated” where applicable; and
- avoid future-tense features in the completed-feature list.

## Pull requests

A good pull request:

- explains the user-visible problem and result;
- links an issue or roadmap seed;
- stays within one coherent scope;
- lists automated tests and their results;
- lists manual Windows/device validation when required;
- includes before/after images only after removing personal data;
- describes migration, privacy, or performance impact; and
- updates documentation and changelog when appropriate.

Complete the repository pull-request template. Draft pull requests are welcome for early architecture or hardware findings.

## Review priorities

Reviewers evaluate:

1. data integrity and recovery;
2. input safety and coordinate correctness;
3. cancellation, disposal, backpressure, and shutdown;
4. privacy and log redaction;
5. failure isolation;
6. automated and hardware test evidence;
7. UI clarity in dark/red modes; and
8. maintainability within project boundaries.

## Security reports

Do not open a public issue for a vulnerability. Follow [SECURITY.md](SECURITY.md).

## License

By submitting a contribution, you agree that it may be distributed under the repository's [MIT License](LICENSE).
