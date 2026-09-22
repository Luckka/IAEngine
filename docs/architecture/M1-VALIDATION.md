# M1 — Validation

## Baseline

- **VERIFIED IN CODE:** 170 tests were recorded before M1: 168 passed and 2 failed.
- The two failures are the existing `ValidationRunnerTests` cases documented in `CURRENT-ARCHITECTURE.md`; neither test was changed.

## M1 additions

`tests/M1CoreBoundaryTests.cs` adds ten deterministic tests covering:

- neutral generic profile defaults;
- explicit OnlineOS profile data;
- forbidden-token inspection for selected generic Core files;
- successful state path and authorized `HUMAN_REQUIRED` recovery;
- compatibility retry/remediation/timeout limits;
- absence of an OnlineOS application assembly reference.
- neutral complete application configuration;
- ignored OnlineOS environment overrides for generic profiles;
- missing profile rejection;
- incomplete explicit OnlineOS profile rejection;
- no-provider generic composition planning.
- explicit provider/capability registration and configuration-path precedence.

Final execution after the M1 changes: **180 total, 178 passed, 2 failed**. The ten
new M1 tests passed. The two failures are the same baseline failures, with no
change to their source tests or to `ValidationRunner`:

1. `FlutterProfileRunsEachConfiguredCategoryFromAppRoot` expects the fake device
   probe to make the integration result pass, but the current result is
   `NotExecutable`.
2. `IntegrationWithoutTargetIsExplicitlyNotExecutable` expects `NotExecutable`,
   but the current result is `NotApplicable` when `integration_test` is absent.

## Required commands

The following were run in the Engine destination only:

- `dotnet build`
- `dotnet test`

- `dotnet build`: passed, 0 warnings, 0 errors.
- `dotnet test tests/OnlineOs.AiOrchestrator.Tests.csproj`: 180 total, 178 passed, 2 failed (the documented baseline cases above).

## Scope limitations

- **VERIFIED IN CODE:** no command was run in the OnlineOS source directory.
- **VERIFIED IN CODE:** no Git repository was initialized in the Engine and no commit was created.
- **VERIFIED IN CODE:** Git lifecycle behavior is characterized by existing tests, but the copied Engine root itself has no Git metadata.
- **UNKNOWN:** emulator/Flutter/Patrol runtime behavior cannot be validated from this destination because the app is intentionally absent.
- **VERIFIED IN CODE:** generic configuration is rejected from operational host commands unless an explicit provider composition is registered; this prevents the current OnlineOS providers and prompts from being loaded by a generic profile.
- **VERIFIED IN CODE:** compatibility code still contains OnlineOS-specific prompt, policy, reference and Git mechanics; moving those units behind separately owned adapters remains deferred.
- **VERIFIED IN CODE:** the composition contract is declarative and testable but does not yet instantiate a generic end-to-end runtime; the executable remains fail-closed for non-OnlineOS operational commands.
- **HUMAN DECISION REQUIRED:** an independent human or second-agent review is still required by the project definition of done.
