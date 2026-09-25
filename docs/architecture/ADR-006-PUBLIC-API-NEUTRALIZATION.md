# ADR-006 — Public API neutralization and compatibility

- Status: proposed / awaiting human decision
- Date: 2026-09-24
- Scope: `IAEngine.Core` and consumers

## Context

M6 separated the Core assembly from the OnlineOS adapter, but the Core's public
namespace and several serialized models retain historical OnlineOS-era names.
The CLI and InfraSentinel currently consume those contracts. The public records
are sealed and are embedded in async contract signatures, host context,
persistence and milestone state.

## Decision

For M7, keep the existing `OnlineOs.AiOrchestrator.*` contracts as a temporary
compatibility surface. Do not perform a mass namespace rename, do not add
partial wrappers, do not mark still-used contracts obsolete, and do not change
the OnlineOS checkout. Keep OnlineOS-specific QA/prototype contracts in the
adapter boundary.

The proposed long-term convention is:

```text
IAEngine.Core.Hosting
IAEngine.Core.Composition
IAEngine.Core.Orchestration
IAEngine.Core.Milestones
IAEngine.Core.Contracts
IAEngine.Core.Persistence
IAEngine.Core.Models
```

If approved, this convention must be introduced as a complete neutral façade or
as a coordinated major-version migration. The façade must define neutral models
and explicit adapters for providers, validators, policies, Git and persistence;
it must not be implemented with public aliases or duplicated partial contracts.

## Consequences

- CLI and current InfraSentinel consumers remain source and binary compatible.
- No NuGet publication or merge to `main` is needed.
- Historical names remain visible and require a later migration.
- A human must choose whether the next step is a complete façade or a major
  namespace migration before code contracts are changed.

## Alternatives rejected

- Destructive mass rename: breaks current consumers and serialized state.
- Public `using` aliases: aliases are not exported compatibility metadata.
- Partial wrappers: create conversion drift and duplicate domain semantics.
- Moving QA/prototype models into Core: reverses the M6 dependency boundary.
