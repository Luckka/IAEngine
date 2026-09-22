# Open Decisions

No decision in this document was silently selected. Items are intentionally left for human architecture approval.

## Decision register

| ID | Decision | Current evidence | Options | Provisional recommendation | Status |
|---|---|---|---|---|---|
| D-001 | Engine identity and compatibility | Current project/namespace is `OnlineOs.AiOrchestrator`; no `.sln`; no Git in destination | Preserve namespace; façade; breaking rename | Keep compatibility façade until characterization and consumer inventory complete | HUMAN DECISION REQUIRED |
| D-002 | Configuration format | Current `ConfigLoader` reads JSON with deterministic explicit-path precedence and `ONLINEOS_*` overrides only for the OnlineOS profile | JSON; YAML; C#; hybrid | JSON schema + optional C# composition for advanced adapters; do not implement yet | HUMAN DECISION REQUIRED |
| D-003 | Project adapter shape | `Project.Composition` and `EngineCompositionPlan` now expose a small declarative capability boundary; runtime provider contracts remain deferred | One bundle; small contracts; plugin discovery | Small explicit contracts plus host-side profile object; no god interface | HUMAN DECISION REQUIRED |
| D-004 | Assembly timing | Current baseline is one executable project and one test project | Same project first; immediate multi-assembly split | Same project first with dependency tests; split after seams are proven | HUMAN DECISION REQUIRED |
| D-005 | Prompt ownership | Current Ollama/Claude/Codex prompts explicitly name OnlineOS/Flutter/policies; generic composition does not activate them | Keep in provider; project context provider; config templates | Project-owned prompt/context provider; preserve exact current prompts during compatibility | HUMAN DECISION REQUIRED |
| D-006 | Validator ownership | Main validation and Patrol QA are separate current paths; generic composition currently registers neither | Core validator registry; QA adapter; separate host command | Generic deterministic command validator in Engine; Flutter/Patrol in OnlineOS adapter | HUMAN DECISION REQUIRED |
| D-007 | Git policy ownership | `GitWorkflowManager` stages OnlineOS paths and assumes `developer`/`producao` etc.; generic option defaults are now neutral | Generic Git policy; project adapter; no Git in Core | Generic mechanics + fail-closed project `IGitWorkflowPolicy` | HUMAN DECISION REQUIRED |
| D-008 | Artifact compatibility | `RunStore` persists `.ai-runs` artifacts and recovery reloads them | Preserve schema; versioned migration; new namespace | Preserve existing schema behind façade, add explicit version only in a later approved phase | HUMAN DECISION REQUIRED |
| D-009 | CodeUp proof scope | No CodeUp implementation or requirements in baseline | Minimal external fixture; real integration; defer | Minimal fixture that proves no OnlineOS references; unknown CodeUp requirements remain UNKNOWN | HUMAN DECISION REQUIRED |
| D-010 | FinGuard scope | No FinGuard implementation or requirements in baseline | Generic gate proof; real adapter design; defer | Generic gate mechanics only; all business/financial rules adapter-owned | HUMAN DECISION REQUIRED |
| D-011 | Distribution | No `.sln`, package manifest or published artifact exists | Independent repo; .NET tool; NuGet; hybrid | Independent source repo + local tool host + few packages only after API stabilizes | HUMAN DECISION REQUIRED |
| D-012 | Baseline test failures | 168/170 passed; two `ValidationRunnerTests` failures are present | Fix now; characterize; waive temporarily | Characterize and resolve ownership before extraction gate; no fix in this task | HUMAN DECISION REQUIRED |

## Configuration comparison

### VERIFIED IN CODE

Current configuration is JSON (`appsettings.json`, `appsettings.example.json`) plus environment variables; there is no YAML/C# configuration loader.

| Option | Strengths | Risks |
|---|---|---|
| JSON | Existing loader, portable, easy to validate/version | Awkward for executable adapters and secrets; schema needed |
| YAML | Human-friendly and expressive | New parser/dependency, ambiguity and formatting differences |
| C# programmatic | Strong typing, composition and custom adapters | Requires compiling consumer code; harder non-.NET onboarding |
| Hybrid | Stable JSON for data + C# for advanced composition | Two configuration paths and lifecycle complexity |

### RECOMMENDED — HUMAN DECISION REQUIRED

Use JSON for project identity/workspace/tasks/milestones/commands/limits and an explicit host composition mechanism for validators, providers and policies. Do not put OnlineOS values into Engine defaults. This is a recommendation, not a selected implementation.

## Distribution comparison

### VERIFIED IN CODE

The baseline is a standalone executable project targeting `net10.0`; no solution or package publication exists.

| Modelo | Isolamento | Versionamento/debug | Consumidores | Risco |
|---|---|---|---|---|
| Repositório/solução independente | Strong source isolation, easiest stepping | Best local debugging; explicit upgrades | Adapter developers | Consumers manage checkout/build |
| `.NET Global/Local Tool` | Strong CLI isolation | Easy install/version pinning; debugging less direct | CLI consumers | Tool/package host must stabilize |
| NuGet packages | Strong API/assembly boundaries | Versioned rollback; dependency/package explosion possible | Embedded hosts/adapters | Public API and package matrix burden |
| Hybrid | CLI plus stable libraries/adapters | Flexible but more release coordination | Mixed consumers | Highest operational complexity |

### RECOMMENDED — HUMAN DECISION REQUIRED

Provisional path: independent Engine repository/solution first; later a local .NET tool for the host and a small number of NuGet packages only after the public contracts and compatibility façade stabilize. Do not publish now.

## Unknowns that must not become defaults

### UNKNOWN

- CodeUp task source, supported stack, required providers and security model.
- FinGuard domain invariants, approval roles, ADR format and challenger behavior.
- External consumers of CLI output, `.ai-runs`/`.ai-state` JSON, namespace or assembly name.
- Required .NET support matrix beyond `net10.0`.
- Plugin discovery, remote execution and multi-project concurrency requirements.
