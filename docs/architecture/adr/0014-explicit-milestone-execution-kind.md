# ADR-0014: Explicit Milestone Execution Kind

## Status

Accepted

## Decision

Route reference validation only when `MilestoneExecutionKind.ReferenceValidation` is explicitly declared. All other milestones use `Normal` by default.

## Context

The previous implementation inferred workflow intent from task text. This caused unrelated milestones containing words such as `read-only` and `validation` to skip the normal EngineHost pipeline.

## Consequences

- Milestone names and descriptions are descriptive data, not routing instructions.
- Existing reference-validation consumers must declare their intent explicitly.
- The public historical namespace remains unchanged for compatibility.
- No consumer-specific rule is added to the Core.
