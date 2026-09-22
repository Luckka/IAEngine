# Coupling Map

Analysis of the isolated baseline. Labels have the following meaning: **VERIFIED IN CODE**, **INFERRED**, **RECOMMENDED**, **UNKNOWN**, **HUMAN DECISION REQUIRED**.

## Coupling inventory

| Acoplamento | Evidência | Categoria | Deve permanecer? | Destino futuro | Risco |
|---|---|---|---|---|---|
| Product namespace/assembly | `OnlineOs.AiOrchestrator.csproj`, `namespace OnlineOs.AiOrchestrator.*` in all source files | OnlineOS-specific coupling | No in generalized identity; yes temporarily for compatibility | Engine assembly/namespace behind compatibility façade | Breaking references and serialized type assumptions |
| CLI repository discovery | `Program.FindRepository` requires `.git` and prints “OnlineOS repository” | CLI/host + OnlineOS-specific coupling | Generic repository discovery, product text no | Host/workspace provider | Isolated Engine cannot run without a Git workspace |
| Ollama route prompt | `Agents/OllamaTaskRouter.cs:21-37` says “local OnlineOS task router” and hard-codes OnlineOS domains, skills and docs | Agent/provider + project policy | Provider mechanics yes; prompt/index no | Project context/router policy supplied by adapter/config | Prompt behavior changes and route drift |
| Claude implementation prompt | `Agents/ClaudeAgent.cs:24-33,38-54` names OnlineOS repository, `AGENTS.md`, `CLAUDE.md`, Flutter and local policies | Agent/provider + project policy | Structured invocation/parser yes; product instructions no | Project prompt/context policy | Security boundary and agent autonomy can leak |
| Codex quality rubric | `Agents/CodexAgent.cs` and `EngineeringStandardsPolicy.cs` encode OnlineOS engineering categories and prototype evidence | Agent/provider + project policy | Review protocol mechanics; rubric supplied by project | Review policy/profile adapter | Generic core may accidentally enforce mobile UX |
| Flutter/Dart validation | `Configuration/OrchestratorOptions.cs:81-104`, `ValidationRunner.cs:69-102`, tests | Technology adapter + OnlineOS-specific coupling | No in Core | OnlineOS validator adapter | Non-Flutter consumers inherit wrong categories |
| Patrol/ADB/emulator | `Pipeline/Patrol*`, `Adb*`, `QaOptions`, `Program` QA branch | Technology adapter | No in Core | OnlineOS QA adapter | Large extraction surface and device side effects |
| Prototype conformity | `PrototypeEvidenceGate`, `VisualConformityReviewGate`, `PrototypeReviewEvidence`, review tests | Project policy + deterministic validator | No in Core; generic evidence gate may stay | OnlineOS review/validator policy | Product authority may be lost or made generic incorrectly |
| Responsive/accessibility/text scaling/design system | `ClaudeAgent.BuildTaskSpecificInstructions`, `EngineeringStandardsPolicy`, tests | Project policy | No as global default | OnlineOS engineering profile/prompt/policy | False requirements for CodeUp/FinGuard |
| Backend reference | `Reference/ReferenceInspection.cs`, `ReferenceOptions.RequiredBranch=b1208`, `ONLINEOS_REFERENCE_ROOT` | OnlineOS-specific coupling | No in Core | OnlineOS reference adapter | Read-only boundary and branch evidence are safety-sensitive |
| `b1208` branch | `ReferenceInspection.cs:19-24`, roadmap JSON `backendReference` and `referenceBranch` | Project policy | No global default | OnlineOS reference policy/config | Silent contract drift |
| `developer`, `producao`, `feature/*` | `GitOptions.ProtectedBranches`, `GitWorkflowManager`, roadmap branches | Project policy + Git abstraction | Generic branch safety yes; names/workflow no | Project Git policy | Wrong branch protections in another project |
| Git staging paths | `GitWorkflowManager.StageMilestoneFilesAsync`: `app`, `tools/ai-orchestrator`, `ai`, `architecture`, `docs`, `README.md`, `.gitignore` | OnlineOS-specific coupling | No in Core | `IGitWorkflowPolicy` implementation | Accidental omission or staging of consumer files |
| `.ai-runs` | `RunStore`, CLI, options | Persistence/infrastructure | Yes as configurable run store concept | Core persistence with project-root namespace | Cross-project state collision if root is shared |
| `.ai-state` | roadmap/reference/audit/Git lifecycle stores | Persistence/infrastructure + project policy | Generic state root yes; artifact schemas no | Namespaced state stores/adapters | State schema incompatibility and stale recovery |
| Roadmap JSON | `ai/roadmap/MILESTONES.json`, `RoadmapCatalog`, `MilestoneRunner` | Project adapter + CLI/host | Generic source/execution mechanics yes | `IMilestoneSource`/project roadmap | Core tied to project task schema |
| OnlineOS roadmap/REQ IDs | `ai/roadmap/MILESTONES.json`, copied `AGENTS.md`, `CLAUDE.md` | Project policy | No in Core | OnlineOS context/policy | Product assumptions become invisible defaults |
| Flutter commands | `appsettings.json`, tests, README | Technology adapter | No in Core | Configured validators | Consumers need non-Flutter command sets |
| `OnlineOS_QA_CHECKPOINT` | `Pipeline/AdbDeviceArtifactCapture.cs` | OnlineOS-specific coupling | No in Core | OnlineOS QA adapter | Artifact protocol collision |
| `OnlineOS-QA` device | `QaOptions.Device` and tests | OnlineOS-specific coupling | No in Core | Project QA config | Wrong emulator/device selection |
| Human-required semantics | `WorkflowState.HumanRequired`, `RecoveryPolicy`, Git workflow | Engine Core generic | Yes | Core policy/gate mechanics | Policy reason taxonomy must remain extensible |
| AWS, CodeUp, FinGuard | No implementation or configuration found | UNKNOWN | No current decision | Future adapters only | Premature interfaces/requirements invention |

## Responsibility classification

### Core candidates — VERIFIED IN CODE / INFERRED

The following behavior is sufficiently project-independent in concept: `WorkflowStateMachine`, `RunRecord` lifecycle mechanics (after policy fields are separated), `FailureClassifier` mechanics, `RecoveryPolicy` mechanics, `RunHealthEvaluator`, `RunStore` atomic artifact mechanics, `ProcessRunner`, `WorkspaceBoundary`, cancellation/timeout propagation, generic gate outcomes, and orchestration sequencing.

This is an **INFERRED** classification: the current implementations still contain OnlineOS types, prompts or policy names and therefore are not clean Core assemblies yet.

### Provider/technology candidates — VERIFIED IN CODE

`OllamaTaskRouter`, `ClaudeAgent`, `CodexAgent`, `PatrolE2ETestRunner`, `PatrolDeviceManager`, `AdbDeviceArtifactCapture`, `AdbVideoCapture`, `PatrolExecutableResolver` and `ProcessRunner` are provider/technology implementations. Their invocation/parsing mechanics can be retained; task vocabulary, context and prompt policy must be externalized.

### Project policy candidates — VERIFIED IN CODE

`EngineeringStandardsPolicy`, prototype evidence gates, `ReferenceInspection`, `RoadmapCatalog` data, Flutter/Patrol command defaults, OnlineOS protected branch names, QA device names, and OnlineOS-specific prompt sections are project policy or adapter material.

### Mixed responsibilities — VERIFIED IN CODE

| Current unit | Mixed concern |
|---|---|
| `Program.cs` | CLI parsing, dependency composition, OnlineOS repository discovery, QA orchestration, run recovery and output formatting. |
| `Orchestrator` | Generic stage loop plus OnlineOS reference preparation, engineering policy profile, provider-specific recovery categories, Git branch checks and artifact names. |
| `RunRecord` | Generic state/checkpoint plus engineering rubric, prototype/backend artifact paths, provider counters and Git lifecycle data. |
| `Configuration/OrchestratorOptions.cs` | Core retry/timeouts plus Ollama/Claude/Codex/Flutter/Patrol/OnlineOS environment variables and defaults. |
| `ValidationRunner` | Generic command runner plus Flutter project/device semantics and OnlineOS workspace wording. |
| `GitWorkflowManager` | Generic lifecycle gates plus OnlineOS branch/path/staging assumptions. |
| `Reference/ReferenceInspection.cs` | Generic read-only reference concept plus monolith/b1208/OnlineOS artifact schema. |

## Explicit search results

### VERIFIED IN CODE

- OnlineOS, Flutter, Dart, Patrol, prototype, responsive, accessibility, text scaling and design system occur in prompts, policies, QA, tests and configuration.
- `b1208`, `developer`, `producao`, `main`, `master` and `feature/*` assumptions occur in reference/Git configuration, roadmap data and Git tests.
- No implementation of AWS, CodeUp or FinGuard was found.
- No `.ai-runs` or `.ai-state` historical data was copied.
- The OnlineOS source repository was only read for branch/status/commit confirmation; it was not written, checked out, formatted, committed, pushed, pulled or otherwise changed.

### UNKNOWN

The intended public API, package boundaries, supported .NET versions beyond this net10 baseline, consumer release cadence, and whether providers are compile-time or runtime selected are not specified by the baseline.

## Extraction implications

### RECOMMENDED

1. Keep a compatibility executable and tests while moving project defaults out of the core.
2. Extract configuration and policy seams before moving classes between assemblies.
3. Treat every current OnlineOS default as explicit adapter configuration; do not preserve it as a Core default.
4. Use dependency-graph tests to fail if Core references `OnlineOs`, Flutter, Patrol, `b1208`, or project prompt paths.

### HUMAN DECISION REQUIRED

The human owner must choose whether compatibility is source-level (same namespace), binary-level (façade), or behavioral only. This materially affects migration order and package/API design.

