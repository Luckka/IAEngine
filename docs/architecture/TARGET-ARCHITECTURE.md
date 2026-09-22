# Target Architecture — Proposal Only

This document proposes a future architecture. It does not implement extraction, adapters, assembly moves, prompt changes, command changes or state-machine changes.

## Design rule

### RECOMMENDED

The Engine owns mechanics and lifecycle. A project adapter owns project vocabulary, prompts, context, validators, policies, commands, roadmap, reference sources and Git conventions. A technology/provider integration owns process/API invocation. No Core code should know Flutter, Patrol, OnlineOS, FinGuard, AWS, CodeUp, `b1208`, financial rules or product requirements.

### HUMAN DECISION REQUIRED

The exact assembly split and compatibility strategy must be approved before changing namespaces or project files.

## Proposed conceptual layout

```text
AI Engineering Engine
├── Core
│   ├── Models and lifecycle values
│   ├── workflow state machine (behavior preserved)
│   ├── gate outcomes and HumanRequired
│   ├── cancellation, timeout and retry mechanics
│   └── workspace-safe process abstractions
├── Orchestration
│   ├── task run coordinator
│   ├── milestone coordinator
│   ├── recovery/health evaluation
│   └── context selection pipeline
├── Agents
│   ├── agent invocation contracts
│   ├── implementation/remediation/review protocols
│   └── provider-neutral result parsing
├── Provider integrations
│   ├── Ollama/Qwen router
│   ├── Claude CLI agent
│   └── Codex CLI reviewer
├── Persistence
│   ├── run state/artifacts
│   ├── roadmap state
│   └── pointer/recovery/audit storage
├── Git abstractions
│   ├── read-only Git facts
│   ├── lifecycle mechanics
│   └── policy-driven branch/staging/merge gates
├── CLI/host
│   ├── command parsing
│   ├── composition root
│   └── output/progress
├── Policies
│   ├── retry/remediation policy
│   ├── review policy
│   ├── preflight policy
│   └── HumanRequired policy
└── Validators
    ├── configured command validator
    ├── deterministic gate aggregation
    └── explicit validator registration

OnlineOS adapter
├── Flutter validators
├── Patrol/ADB validator and device lifecycle
├── prototype conformity/evidence policy
├── b1208 read-only reference policy
├── OnlineOS Git policy
├── OnlineOS context/prompt provider
└── roadmap/requirements mapping

FinGuard adapter (future)
├── ADR/learning-mode policy
├── architecture decision/defense gates
├── financial invariant validators
├── architecture challenger
└── human approval policy
```

## Module responsibilities and boundaries

| Módulo futuro | Responsabilidade | Permitido | Proibido | Classes que migrariam | Assembly agora? | Risco |
|---|---|---|---|---|---|---|
| Core | State, task/run values, gate outcomes, cancellation and generic errors | BCL and stable contracts | Provider SDKs, product names, prompts, Flutter/Patrol | Parts of `Models/WorkflowModels.cs`, `WorkflowStateMachine`, `WorkspaceBoundary` | No; same project first | Coupling hidden in models/serialization |
| Orchestration | Execute current loop and milestones without changing transitions/limits | Core contracts, policies, stores, agents | Direct HTTP/CLI, product policy | `Pipeline/Orchestrator`, `Roadmap/MilestoneRunner`, `RunHealthEvaluator` | No; same project first | Behavior regression in recovery |
| Agents | Provider-neutral implementation/review/remediation protocol | Agent contracts and context | OnlineOS prompt text in Core | `Abstractions/Contracts.cs`, protocol portions of `ClaudeAgent`, `CodexAgent` | No initially | Interfaces may be too broad |
| Provider integrations | Ollama/Claude/Codex invocation | HTTP/process adapters | Core state transitions and project requirements | `OllamaTaskRouter`, `ClaudeAgent`, `CodexAgent` | Optional later assembly | Provider output drift |
| Persistence | Durable run/artifact/state storage | Core models, workspace storage abstraction | Product-specific artifact schemas | `RunStore`, `GitLifecycleStore`, `RoadmapStateStore` | No initially | Migration of JSON compatibility |
| Git abstractions | Git facts and lifecycle mechanics | `IGitService`, generic lifecycle policy | Hard-coded branch/path list | `GitService`, `GitWorkflowManager` | No initially | Unsafe defaults if policy missing |
| Policies | Decision rules supplied explicitly per project | Core facts and config | Global OnlineOS defaults | `ReviewPolicy`, `EngineeringStandardsPolicy`, `RecoveryPolicy` | Same project first | Policy leakage |
| Validators | Deterministic command/gate execution | Registered commands and target workspace | Flutter assumptions in generic runner | `ValidationRunner`, genericized QA gate pieces | No; split after characterization | Gate status semantics drift |
| CLI/host | Parse commands and compose adapters | Host configuration and output | Core knowledge of OnlineOS | `Program.cs`, `ProgressReporter` | No; keep compatibility host | Composition remains monolithic |
| OnlineOS adapter | Preserve current product behavior | Engine extension contracts, Flutter/Patrol tooling | Core dependency on OnlineOS | Current Flutter/Patrol/reference/prompt/policy units | No, later | Adapter may retain accidental broad access |
| FinGuard adapter | Future decision/financial gates | Generic policy/gate contracts | Core knowledge of finance | None yet | No | Requirements are unknown |

## Runtime flow

### VERIFIED IN CODE

The current order is preserved in the proposal: route → implement → deterministic validate → review → remediate → validate/review again → terminal decision. `HumanRequired` and recovery behavior are not being redesigned here.

### RECOMMENDED

The future host should compose a `ProjectProfile` (identity, workspace, context, tasks, milestones, validators, policies and Git policy) and a `ProviderProfile` (router/implementation/reviewer). The orchestrator consumes those resolved components. It should not inspect project names or infer a project from a path.

## Assembly decision

### RECOMMENDED

Keep one executable/project during the first extraction steps. Introduce conceptual namespaces/folders and dependency tests before splitting assemblies. Split `Engine.Core`, `Engine.Providers`, `Engine.Persistence`, `Engine.Host`, and adapters only after a build proves the dependency direction and compatibility façade.

### HUMAN DECISION REQUIRED

Whether to create separate assemblies immediately is not selected. Immediate splitting improves enforcement but increases package/reference/debugging risk; same-project separation reduces migration risk but permits accidental coupling.

## CodeUp generality proof

### UNKNOWN

No CodeUp requirements, task schema, provider requirements or security model are present in the baseline. The proof below is intentionally a generic consumer fixture, not a CodeUp implementation.

### RECOMMENDED

Create a trivial external consumer fixture that submits one task and uses a no-op or explicitly registered deterministic validator. The proof should assert:

- the Core build/reference graph contains no `OnlineOs`, Flutter, Dart, Patrol, prototype, `b1208`, or OnlineOS-policy assembly/reference;
- the consumer runs without loading OnlineOS assemblies or files;
- validators are discovered only through explicit registration, and an unregistered validator is not executed;
- a project policy registered for one consumer is not visible to another consumer;
- `.ai-runs`/`.ai-state` roots, prompts/context and workspace paths are isolated between two runs/projects;
- invalid workspace/provider/validator configuration fails during preflight before an agent starts;
- the task reaches the same generic terminal mechanics (`Approved`, `Remediation`, or `HUMAN_REQUIRED`) without any OnlineOS-specific decision.

These are **RECOMMENDED tests**, not CodeUp requirements. Any CodeUp-specific behavior remains **UNKNOWN** until supplied by its owner.

## FinGuard generality proof

### RECOMMENDED

The Engine should expose only the generic decision mechanic:

```text
Policy evaluation
      ↓
Gate result
      ↓
Approved | Remediation | HUMAN_REQUIRED
```

The future FinGuard adapter may implement Learning Mode, mandatory ADR, Architecture Decision Gate, Architecture Defense Gate, architecture challenger, human approval and financial invariant gates. Those rules, data, thresholds and approval roles must remain outside Core. A fixture should prove that a policy can select a gate outcome and that the orchestrator persists/remediates/pauses it without knowing the policy domain.

### UNKNOWN / HUMAN DECISION REQUIRED

FinGuard’s actual invariants, ADR format, approval authority and challenger protocol are not provided. No financial default, AWS rule or FinGuard requirement should be invented in the Engine.

## Non-goals of this phase

### VERIFIED IN CODE / HUMAN DECISION REQUIRED

No adapter, provider implementation rewrite, CodeUp, FinGuard, package, tool distribution, prompt change, command change, state-machine change or retry-limit change is proposed as an implementation in this analysis.
