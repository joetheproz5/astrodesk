## Outcome

Describe the user-visible result and the shooting-workflow problem it solves.

## Related work

Link the GitHub issue or seed ID from `.github/ISSUES_TO_CREATE.md`.

## Changes

- Describe the implementation.

## Verification

- [ ] `dotnet format AstroDesk.sln --no-restore --verify-no-changes`
- [ ] `dotnet build AstroDesk.sln --configuration Release`
- [ ] `dotnet test AstroDesk.sln --configuration Release`
- [ ] New or changed deterministic behavior has automated tests.
- [ ] Required Windows/device validation is documented below, or is not applicable.

### Manual validation

List Windows build, display scaling/topology, phone/Android, ADB, scrcpy, steps, and result when relevant. Do not include the raw device serial.

## Risk and recovery

Describe capture/input, persistence/migration, privacy, performance, cancellation/disposal, or compatibility risks and how a user recovers if the change fails.

## Documentation

- [ ] README/integration/development documentation is updated when behavior changed.
- [ ] `CHANGELOG.md` is updated for a user-visible change.
- [ ] Experimental behavior is clearly labeled and disabled by default.
- [ ] No AI, account, social, analytics, advertising, or telemetry feature is introduced.
- [ ] No credentials, private device data, or machine-specific paths are included.
