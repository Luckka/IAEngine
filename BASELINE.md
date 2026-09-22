# AI Engineering Engine — Baseline

## Provenance

- `DISCOVERED_SOURCE_ORCHESTRATOR`: external OnlineOS source, path intentionally omitted from this public repository
- Source repository root: external OnlineOS source, read-only input
- Source branch: `remediation/o01-visual-conformity`
- Source commit: `724030b2e8fc71c6fc7356f98b676a233eccfaa7`
- Source working tree before copy: clean (`git status --short` produced no file changes)
- Copy date: `2026-09-20`
- .NET SDK: `10.0.301`
- Destination: this repository

## Purpose

This directory is an isolated baseline copy of the OnlineOS AI orchestrator for
future independent evolution. The copy preserves the source project structure,
tests, providers, workflow state machine, persistence, recovery, validation,
review, remediation, QA pipeline, roadmap support, and Git workflow code.

The OnlineOS repository was used as read-only input and was not modified.

## Confirmed behavior to preserve

- CLI routing through Ollama/Qwen with deterministic fallback.
- Claude implementation, deterministic validation, Codex review, and Claude remediation.
- Persisted workflow states and transitions, including `HumanRequired`, `Approved`, and `Failed`.
- Retry, timeout, cancellation, recovery, health evaluation, and remediation limits.
- Run artifacts under `.ai-runs` and milestone state under `.ai-state` when explicitly executed.
- Preflight, task, task-file, continue, status, audit, retry, abandon, QA, and milestone commands.
- Deterministic review policy, engineering standards profile, and validation expectations.
- Git lifecycle orchestration and its human-required safety gates.

## Known limitations of this baseline

- No Git repository metadata is present in the destination by design. Git-dependent
  execution and milestone finalization therefore remain intentionally unavailable
  until a later human-approved workspace/Git decision.
- The copied Git workflow still contains OnlineOS-era lifecycle assumptions and
  paths; it is preserved and documented, not generalized in this phase.
- OnlineOS-specific prompts, policy names, roadmap schema, QA device names, and
  validation defaults remain baseline behavior.
- The destination does not contain historical `.ai-runs` or `.ai-state` data.
- The destination does not contain the OnlineOS Flutter application or the full
  documentation tree referenced by the router and provider prompts.

## Scope boundary

No FinGuard, CodeUp, packages, NuGet changes, plugins, complete adapters, new
configuration system, multi-assembly architecture, .NET tool packaging, publishing,
new worktrees, or new state machine was introduced. Human approval is required
before the next extraction/generalization phase.
