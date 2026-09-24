# M5 — Package Surface Review

Status: **BLOCKED — architectural decision required before packaging**

## Current project shape

`OnlineOs.AiOrchestrator.csproj` targets `net10.0` with `OutputType=Exe`.
There is no library project containing only the generic Engine. The executable
assembly compiles the generic host and workflow together with the historical
OnlineOS compatibility CLI, providers, validators, Patrol/ADB integration and
reference-specific code.

This means a normal `dotnet pack` would package one assembly containing both
the reusable host and technology/product-specific implementation. A consumer
could reference the host types, but the package would not yet be a clean
technology-neutral distribution boundary.

## API currently consumed by InfraSentinel

The current consumer uses these public contracts and implementations:

- `Hosting.EngineHost` and `Hosting.EngineHostContext`;
- `Hosting.EngineExecutionResult` and
  `Hosting.EngineMilestoneExecutionResult`;
- `Hosting.IEngineTaskSource` and `Hosting.IEngineMilestoneSource`;
- `Configuration.EngineCompositionPlan`, `IProjectComposition`,
  `EngineCompositionRuntimeBuilder` and runtime status types;
- `Configuration.AppOptions`, `ProjectProfileOptions` and orchestration options;
- `Abstractions.ITaskRouter`, `IImplementationAgent`, `IReviewAgent`,
  `IValidationRunner`, `IGitService`, `IRunStore` and related contracts;
- `Models.DevelopmentTask`, validation/review/run result models;
- `Roadmap.MilestoneDefinition`, `RoadmapTaskDefinition` and runtime status
  models;
- `Infrastructure.RunStore` and roadmap state persistence types.

The consumer's own validator and fakes implement Engine abstractions. It does
not need the concrete OnlineOS router, Claude/Ollama/Codex agents, Patrol, ADB,
reference inspector or compatibility CLI.

## Types that should not define the package boundary

The following are implementation details or technology/product adapters and
should not be required by a clean package surface:

- top-level `Program.cs` and CLI dispatch;
- `Agents/` implementations for external providers;
- Patrol, ADB, Flutter and prototype evidence implementations;
- OnlineOS reference inspection and compatibility Git/policy behavior;
- OnlineOS-specific configuration defaults and resources;
- executable-only output and runtime configuration files.

Some of these types are currently public because the monolithic assembly
predates the package boundary. Making them internal or moving them would be a
public compatibility decision and is not performed in M5.

## Dependencies and package contents

The project has no explicit third-party `PackageReference`; its direct build
dependency is the .NET 10 framework. However, the assembly itself contains all
compiled source files in this repository. A conventional package would
therefore contain the Engine executable assembly and expose its transitive
implementation surface, including historical OnlineOS code.

The intended local package must eventually contain only:

- the reviewed reusable Engine assembly or assemblies;
- XML documentation and symbols when useful;
- package README and license metadata;
- no `.ai-runs`, `.ai-state`, `bin`, `obj`, personal paths, secrets, OnlineOS
  checkout, Flutter app or consumer fixtures.

## Risks

1. Packaging the current executable would distribute OnlineOS implementation
   accidentally.
2. The assembly name and `OnlineOs.AiOrchestrator` namespace are historical and
   do not communicate a technology-neutral public package.
3. The broad `EngineHostContext` and concrete filesystem persistence make the
   package surface larger than the minimum host contract.
4. A `PackageReference` could appear to decouple repositories while still
   shipping an unsafe monolithic assembly.
5. A local feed can make restore reproducible, but it does not solve API or
   assembly separation.

## Decision for M5

Do not run `dotnet pack`, change `OutputType`, or replace InfraSentinel's
`ProjectReference` yet. The package would still include OnlineOS components,
which violates the requested M5 gate. No public-contract refactor is being
performed silently.

The smallest safe next decision is whether to approve a dedicated reusable
Engine assembly, preserving the existing executable as a compatibility host, or
to accept a temporary monolithic local package with an explicit risk waiver.

## Learning Checkpoint

1. `ProjectReference` compiles against a sibling project's source/build graph;
   `PackageReference` restores a versioned artifact from a feed.
2. A versioned package reduces source-tree coupling, but only when its contents
   and public API are safe and reproducible.
3. Publishing before contract stabilization can freeze accidental public types,
   transitive dependencies and compatibility behavior.
4. The package should contain reviewed Engine binaries, metadata and symbols; it
   must exclude runtime state, secrets, build outputs, personal paths, OnlineOS
   checkout and consumer fixtures.
5. `0.1.0-local` identifies an experimental artifact, not a stable API promise.
6. A library is referenced by an application; a .NET Tool is an executable
   distribution invoked through the `dotnet` tool ecosystem. This milestone does
   not introduce either public distribution form.

