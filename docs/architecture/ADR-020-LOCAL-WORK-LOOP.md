# ADR-020 — Local Codex work loop

## Status

Accepted for local, bounded automation.

## Decision

Keep task discovery, status reporting and Codex invocation in
`tools/codex-work-loop`. Project behavior is supplied by a JSON configuration;
the loop does not reference InfraSentinel or product-specific runtime types.

The loop creates no branches and never operates on protected branches. It
requires a clean tree before an autonomous cycle, limits iterations/files/
commits, invokes build and tests sequentially, and stops on failure or a human
decision. Generated reports are ignored local state.

## Consequences

- Prompt copying is removed for explicitly requested local cycles.
- Dry-run remains available without Codex invocation.
- Consumers can use different roadmaps and commands without an assembly
  dependency on IAEngine.Core.
- Automatic execution still requires human review of the generated task and
  resulting commit/working tree.
