# Explicit Milestone Execution Kind

Milestone execution intent is now explicit. `MilestoneExecutionKind.Normal` is the default and is preserved when a milestone name or task description contains words such as `read-only`, `validation`, or `reference`.

Consumers that intentionally need the historical reference-validation short circuit must declare `MilestoneExecutionKind.ReferenceValidation` on the milestone definition. The compiled tasks carry that declaration to the Engine workflow. No product, project, technology, or repository name participates in routing.

This removes accidental routing collisions for ordinary milestones such as `read-only-cloud-observation-validation` while preserving the legacy behavior for callers that explicitly opt in.
