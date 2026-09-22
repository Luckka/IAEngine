# M2 — Runtime Composition Boundary

Status: implemented on the milestone branch; human review required.

## What M2 connects

`Project.Composition` remains the declarative input. `EngineComposition.Create`
turns that input into an `EngineCompositionPlan`, and
`EngineCompositionRuntimeBuilder` requires the host to register each declared
provider, validator, policy, and capability explicitly. `Build()` first checks for
missing registrations, then creates only declared components. No component is
selected by project id, directory, or environment variable.

The resulting `EngineCompositionRuntime` exposes separate lists for declared,
registered, and resolved components and typed resolution methods such as
`ResolveProvider<T>` and `ResolveCapability<T>`.

## OnlineOS compatibility registration

`Program.cs` is the compatibility composition root. When the explicit
`onlineos-mobile` profile is loaded, it registers:

- `ollama`, `claude`, and `codex` providers;
- configured validators and the `validation` capability;
- `qa`, `reference-inspection`, and `git-workflow` capabilities;
- the configured policy names.

The existing agent classes, validation runner, Patrol runner, reference inspector,
Git workflow, state machine, orchestrator, persistence, retries, and limits remain
the implementations used after resolution. Tests use fakes and never start those
providers.

## Generic consumer boundary

A consumer can construct an `EngineCompositionPlan`, call
`CreateRuntimeBuilder()`, register local implementations against the existing
typed contracts, and resolve them without OnlineOS fallback. A missing required
registration raises `CompositionResolutionException` before any factory is
called, which is the fail-closed preflight boundary for external processes and Git.

The current executable still rejects non-OnlineOS profiles for operational CLI
commands because it has no external host-registration input. Therefore this
milestone proves the runtime composition contract and fake-consumer path; it does
not make InfraSentinel or any other external project executable end to end.

Policies are currently registered by explicit name through the small
`IProjectPolicyComponent` marker. Existing policy decisions remain in their typed
implementations. A later milestone may extract executable policy contracts when a
second implementation requires them; M2 does not introduce domain policy into
the Core.

## Verification

`tests/RuntimeCompositionTests.cs` proves generic fake resolution, unused
undeclared registrations, fail-closed missing registrations before factories, and
explicit OnlineOS registration names. Existing workflow, persistence/recovery,
Git-safety, validation, and Flutter characterization tests remain unchanged.
