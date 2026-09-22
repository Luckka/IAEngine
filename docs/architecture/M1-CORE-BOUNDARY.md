# M1 — Core Boundary and Configuration Isolation

Status: implemented pending human review. Scope: Engine only.

## Evidence classification

- **VERIFIED IN CODE** — `WorkflowStateMachine`, `Orchestrator`, `RunStore`, recovery policy and provider contracts were not changed.
- **VERIFIED IN CODE** — `ProjectProfileOptions` and the generic option sections have neutral defaults; OnlineOS values are present only when the explicit `onlineos-mobile` compatibility profile is loaded.
- **VERIFIED IN CODE** — an absent or incomplete profile is rejected before provider composition or external commands; `ONLINEOS_*` environment overrides are ignored for generic profiles.
- **VERIFIED IN CODE** — generic composition registers no validation, QA, reference inspection or OnlineOS policy pipeline; the current executable remains a compatibility host only for the explicit OnlineOS profile.
- **VERIFIED IN CODE** — `Project.Composition` explicitly records providers and capabilities; `EngineCompositionPlan` exposes providers, validators, policies and capabilities for deterministic inspection without instantiating external providers.
- **VERIFIED IN CODE** — configuration precedence is explicit: `ONLINEOS_ORCHESTRATOR_CONFIG`, direct `appsettings.json`, legacy `tools/ai-orchestrator/appsettings.json`, then a missing direct path that produces the profile-required error. The binary directory is not searched.
- **INFERRED** — the existing CLI and artifact JSON are compatibility surfaces because the current persistence and recovery code reads them directly.
- **UNKNOWN** — external consumers of the executable assembly, CLI text and artifact files are not present in this isolated destination.
- **HUMAN DECISION REQUIRED** — future assembly split, adapter contract shape, prompt ownership and distribution remain open.

## Files changed

- `Configuration/ProjectProfile.cs`: small declarative project profile, branch policy, retry/remediation limits and validation.
- `Configuration/OrchestratorOptions.cs`: exposes the profile and preserves all existing option sections and environment overrides.
- `appsettings.json` and `appsettings.example.json`: make the current OnlineOS profile explicit rather than relying on the new generic defaults.
- `tests/M1CoreBoundaryTests.cs`: characterization and deterministic dependency/configuration isolation tests.
- `Models/E2EModels.cs` and `Models/WorkflowModels.cs`: remove OnlineOS device/base-branch defaults from generic model construction; existing OnlineOS JSON continues to provide its explicit values.
- `Program.cs` and `Configuration/OrchestratorOptions.cs`: remove binary-directory configuration fallback and expose deterministic config-path resolution.
- `docs/architecture/M1-*`: implementation, decisions and validation records.

## Configuration extracted

The profile can represent project id, workspace root, stack, commands, validators, policies, branch policy, retry limits, remediation limits, timeout and context paths. It is intentionally a data object; it does not execute commands or own business logic. Existing `Orchestrator`, `Validation`, `Git`, `Reference`, `Qa`, Claude, Codex and Ollama options remain in place to avoid a broad compatibility refactor.

## Defaults and temporary coupling

- Generic `ProjectProfileOptions` defaults to `engine-project`, `.` and `unspecified`, with empty commands/validators/policies/context paths and only `main`/`master` protected by default.
- Generic `ReferenceOptions`, `QaOptions`, `FlutterValidationOptions`, `GitOptions`, and E2E request models no longer default to `b1208`, `OnlineOS-QA`, `app`, Flutter/Patrol commands, or `developer`/`producao`.
- The checked-in OnlineOS compatibility JSON explicitly supplies the prior values, including reference branch, QA device, Flutter paths/commands, and protected branches.
- OnlineOS prompts, policy mechanics, Git staging paths, reference literals and QA implementation remain in compatibility code, but the generic host composition does not register or load them.
- The composition boundary remains data/plan based; provider construction is still the existing OnlineOS compatibility host. A full generic runtime registry is deferred.

## Rollback

Remove the profile property, `ProjectProfile.cs`, the M1 tests and the added profile JSON sections. Existing option sections and runtime behavior remain the compatibility path. No migration of `.ai-runs` or `.ai-state` is required.

## Next milestone

**RECOMMENDED:** M2 should replace the current composition plan with explicit validator/policy/provider contracts after a human decision on prompt ownership and the treatment of the two pre-existing validation failures.
