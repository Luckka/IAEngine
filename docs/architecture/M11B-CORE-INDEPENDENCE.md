# M11-B — IAEngine Core Independence from OnlineOS

## Result

M11-B establishes the physical build boundary for `IAEngine.Core`, but does not
claim that the entire IAEngine repository is free of historical OnlineOS naming.
The public `OnlineOs.AiOrchestrator.*` compatibility surface remains because
existing consumers are not in scope for this milestone. A future major API
decision is required before that surface can be removed.

## Changes

- `IAEngine.Core.csproj` now declares `IAEngine.Core` as its root namespace.
- `Pipeline/GitWorkflowManager.cs` is no longer compiled into `IAEngine.Core`.
- The legacy CLI project compiles `GitWorkflowManager.cs` itself, preserving its
  compatibility behavior outside the Core.
- Generic `GitService` branch protection is driven only by consumer-provided
  `GitOptions`; historical `developer`/`producao` protection is no longer hidden
  in the Core implementation.
- Generic detached-process labeling and workspace-boundary errors no longer carry
  OnlineOS product names.
- Core boundary tests assert the project composition and the absence of an adapter
  assembly reference.

## Classification

| Surface | Classification | M11-B treatment |
|---|---|---|
| EngineHost, state machine, persistence, milestone mechanics | Generic | Kept in Core |
| Git checkpoint contracts and policy | Generic | Kept in Core; no push/merge |
| `GitWorkflowManager` | OnlineOS/CLI compatibility | Excluded from Core; retained in CLI |
| QA, Flutter, Patrol, ADB, prototype and reference inspection | Adapter/compatibility | Kept at the edge, not added to Core compilation |
| `OnlineOs.AiOrchestrator.*` public namespaces | Historical compatibility | Retained pending consumer migration/API-major decision |
| OnlineOS profile validation and legacy options | Historical compatibility | Retained for explicit compatibility profile |

## Dependency direction

The verified project direction is:

```text
IAEngine.OnlineOSAdapter → IAEngine.Core
OnlineOs.AiOrchestrator CLI → IAEngine.Core + IAEngine.OnlineOSAdapter
InfraSentinel → IAEngine.Core
```

`IAEngine.Core` has no project reference to the adapter and does not compile the
legacy Git workflow implementation. InfraSentinel was not changed in M11-B.

## Verification

Commands were run sequentially:

| Command | Result |
|---|---|
| `dotnet build src/IAEngine.Core/IAEngine.Core.csproj` | PASS |
| `dotnet test tests/IAEngine.Core.Tests/IAEngine.Core.Tests.csproj` | PASS — 42 |
| `dotnet build src/IAEngine.OnlineOSAdapter/IAEngine.OnlineOSAdapter.csproj` | PASS |
| `dotnet test tests/IAEngine.OnlineOSAdapter.Tests/IAEngine.OnlineOSAdapter.Tests.csproj` | PASS — 73 |
| `dotnet build OnlineOs.AiOrchestrator.csproj` | PASS |
| `dotnet test tests/OnlineOs.AiOrchestrator.Tests.csproj` | PASS — 97 |

The Core tests include the M11-A checkpoint contract tests and the M11-B boundary
composition tests. No OnlineOS checkout, provider, AWS service, or external
infrastructure is required by these commands.

## Remaining exception and human decision

The Core still exposes historical namespaces and shared compatibility models such
as the explicit OnlineOS profile and legacy Git workflow contracts. InfraSentinel
currently consumes those namespaces directly. Removing or moving them without a
coordinated consumer migration would be a breaking API change and would violate
the M11-B rule against modifying InfraSentinel. Therefore:

```text
HUMAN_REQUIRED = true
M11B_COMPLETED = false
```

The next decision should choose between a major namespace migration, a separately
versioned compatibility assembly, or continued temporary compatibility. The
InfraSentinel coordinator milestone must not start until that decision is made if
full Core namespace neutralization is required.
