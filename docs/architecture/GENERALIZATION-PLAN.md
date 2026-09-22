# Generalization Plan — Future Phases

The phases below are proposals only. None has been executed by this analysis.

## Phase matrix

| Fase | Objetivo | Arquivos afetados (futuros) | Testes/gate | Rollback | Risco | Decisão humana |
|---|---|---|---|---|---|---|
| 1. Characterization | Freeze current behavior, state transitions, recovery limits, artifacts and CLI outputs | Add analysis/characterization tests around `Pipeline/Orchestrator.cs`, `WorkflowStateMachine.cs`, `RunStore.cs`, `Program.cs`; no OnlineOS writes | Existing 170 tests plus deterministic fixtures for every state/recovery branch; baseline comparison | Revert only new tests/fixtures; no runtime change | Existing two failing validation tests must be classified before using suite as clean gate | Approve treatment of the two baseline failures |
| 2. Classification | Mark Core/provider/host/persistence/project/technology/policy responsibilities | `docs/architecture/*`, dependency inventory, possibly namespaces only later | Review coupling map; dependency scan fails on prohibited Core references | Documentation-only rollback | Misclassification hides policy in Core | Approve classification and public API scope |
| 3. Configuration isolation | Move identity/workspace/commands/providers/retries/policies to explicit project profile without changing values | `Configuration/OrchestratorOptions.cs`, `ConfigLoader`, `appsettings*.json`, `Program.cs` | Equivalent resolved config snapshot; invalid config fails preflight; no OnlineOS default in Core | Keep compatibility loader and switch host flag back | Configuration schema drift | Choose JSON/YAML/C#/hybrid; see `OPEN-DECISIONS.md` |
| 4. Policy isolation | Separate retry/remediation/review/engineering/reference/Git policies from mechanics | `FailureDiagnosis.cs`, `ReviewPolicy.cs`, `EngineeringStandardsPolicy.cs`, `GitWorkflowManager.cs` | Current decision table and state-transition tests unchanged; policy provenance in artifacts | Compatibility policy wrappers | Accidentally changes HumanRequired boundaries | Approve policy ownership and failure taxonomy |
| 5. Validator isolation | Make deterministic validators explicit and project-registered; preserve `ValidationResult` semantics | `ValidationRunner.cs`, `PreflightService.cs`, `Pipeline/Prototype*`, QA classes; add validator registration | Core dependency test; validator discovery test; current validation tests; resolve existing two failures first | Keep old `ValidationRunner` as façade | Required/not-executable/not-applicable semantics drift | Decide whether QA is a validator or separate host pipeline |
| 6. Provider abstraction | Keep router/Claude/Codex mechanics but remove project prompt/index from generic paths | `Agents/*.cs`, `Abstractions/Contracts.cs`, context builder, `Program.cs` | Prompt snapshot isolation; provider retry tests; malformed/timeout tests; no state-machine change | Compatibility agents using old prompts | Prompt changes violate explicit task boundary | Approve prompt ownership and provider versioning |
| 7. OnlineOS adapter | Re-home Flutter/Patrol/prototype/reference/b1208/OnlineOS Git and context behavior outside Core | `Pipeline/Patrol*`, `Adb*`, `Reference/*`, `EngineeringStandardsPolicy.cs`, roadmap data, prompts | OnlineOS-only adapter suite; source repo remains independent; CodeUp-style absence test | Keep adapter in same project behind profile | Adapter still leaks OnlineOS types | OnlineOS migration is explicitly deferred; approve adapter API |
| 8. Compatibility façade | Preserve current CLI/schema/assembly behavior while Engine internals evolve | `Program.cs`, assembly/namespace façade, serialization adapters | CLI golden tests, artifact schema tests, current test suite | Restore old composition root | Hidden external consumers not covered | Decide compatibility duration and supported schema |
| 9. CodeUp proof | Prove a trivial consumer can use Core without OnlineOS assemblies/policies | New external fixture/consumer project; no CodeUp implementation assumed | Build/reference graph, explicit validator registration, isolated prompts/workspaces/state, invalid config preflight | Delete proof fixture; no engine behavior change | CodeUp requirements are unknown | Confirm only generic proof scope; mark unknowns |
| 10. FinGuard adapter | Prove generic `Policy → Gate → Approved/Remediation/HUMAN_REQUIRED` supports future governance | New adapter fixture only after requirements exist | Gate contract tests, human approval and architecture challenger fixtures; financial invariants remain adapter-owned | Remove adapter fixture | FinGuard requirements are unknown | Define FinGuard requirements and ownership |
| 11. Distribution | Select repository/tool/NuGet/hybrid and package public surface | `.sln`, package projects, tool manifest/CI only after approval | local install/rollback/consumer build/provider adapter compatibility | Retain source checkout; unpublish not assumed | package explosion/version skew | Final distribution decision required |

## Phase gates

### VERIFIED IN CODE

The current source has no `.sln`, no packages, no adapters and no distribution manifest. The proposed plan therefore begins with evidence and compatibility, not packaging.

### RECOMMENDED

Every phase gate should require: diff read, dependency scan, focused tests, full tests where applicable, artifact/state schema comparison, and an explicit report of `UNKNOWN`/`HUMAN DECISION REQUIRED` items. No phase should alter the OnlineOS repository.

## Preservation constraints

### HUMAN DECISION REQUIRED

Until an owner approves otherwise, preserve:

- current state machine and authorized recovery predicates;
- prompt text and provider commands;
- retry/remediation counts and timeout defaults;
- artifact names and `.ai-runs`/`.ai-state` semantics;
- current OnlineOS behavior behind a compatibility path.

## Open risks

- **VERIFIED IN CODE:** the destination has no Git, so end-to-end Git lifecycle cannot be exercised from this isolated baseline without a later workspace decision.
- **VERIFIED IN CODE:** two validation tests fail in the baseline; a green test gate cannot be claimed yet.
- **INFERRED:** JSON artifact compatibility is likely an implicit consumer contract because recovery loads historical artifacts.
- **UNKNOWN:** external scripts, CI jobs or users that depend on assembly names, CLI text or artifact schemas.

