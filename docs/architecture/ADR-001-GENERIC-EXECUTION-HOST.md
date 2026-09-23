# ADR-001 — Generic local execution host

Status: Proposed; not automatically approved.

## Context

M2 proved explicit runtime composition, but an external project still had to
construct the Engine `Orchestrator` and its infrastructure services manually.
That made a local consumer proof either tightly coupled to internals or
fictitious.

## Decision proposed

Add `EngineHost` as a small composition façade. A consumer supplies project
identity, workspace, `AppOptions`, `IProjectComposition`, component factories,
`IRunStore`, `IGitService`, and the names of the typed provider/capability
contracts required for one run. The host validates identity and configuration,
resolves the declared runtime, then creates the existing `Orchestrator` with
the existing `ReviewPolicy` and `WorkflowStateMachine`.

Task and milestone sources remain consumer-provided contracts. Milestone
execution is not expanded in this milestone; the existing roadmap runner is
preserved until a generic source/execution contract is reviewed.

## Consequences

- Consumers do not copy Engine source or construct internal workflow objects.
- The Engine does not know InfraSentinel, AWS, Flutter, Patrol, or product rules.
- Persistence and Git boundaries remain explicit and consumer-owned.
- The current executable and OnlineOS compatibility composition remain unchanged.
- A local `ProjectReference` proves the contract before packaging decisions.

## Alternatives rejected for this milestone

- CLI-only integration: the current CLI remains an OnlineOS compatibility host.
- NuGet or .NET Tool: distribution is explicitly deferred.
- Dynamic plugin discovery: explicit registration is safer and already exists in M2.
- Copying sources: violates repository separation.
