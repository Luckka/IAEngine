# ADR-005 — Local package consumption

Status: **Proposed and blocked; not approved automatically**

## Context

M5 asks InfraSentinel to consume a local versioned IAEngine package instead of
   a sibling `ProjectReference`. The current IAEngine project is an executable
   monolith containing both the generic host and OnlineOS compatibility code.

## Decision proposed

Use a local NuGet feed and experimental version `0.1.0-local` only after the
package surface is isolated sufficiently to exclude OnlineOS-specific
implementation from the reusable package. The feed must remain outside Git or
under an ignored directory, and no package may be published externally.

## Current gate

The gate is not satisfied. `dotnet pack` of the current project would package
the executable assembly that also contains Flutter, Patrol, ADB, OnlineOS
reference and compatibility implementations. Creating and consuming that
package now would create a misleading boundary.

Therefore M5 stops at the review/documentation stage. InfraSentinel remains on
the reviewed local `ProjectReference` in this branch, and no `.nupkg` is
created.

## Alternatives

- **Dedicated Engine library plus compatibility executable (recommended for
  review):** clean package boundary, but requires an approved assembly/public API
  change.
- **Temporary monolithic local package:** smallest code change, but ships
  unwanted OnlineOS implementation and requires an explicit risk waiver.
- **Continue ProjectReference:** safest compatibility behavior now, but does not
  prove artifact-based repository decoupling.

## Consequences

No public API or consumer build behavior changes in M5. A human architectural
decision is required before implementation can continue.

