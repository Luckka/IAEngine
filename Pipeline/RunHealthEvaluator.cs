using OnlineOs.AiOrchestrator.Models;

namespace OnlineOs.AiOrchestrator.Pipeline;

public static class RunHealthEvaluator
{
    public static RunHealthAssessment Assess(RunRecord run)
    {
        if (run.State is WorkflowState.Approved or WorkflowState.Failed or WorkflowState.Completed or WorkflowState.Abandoned)
            return new(RunHealth.Terminal, $"Run is terminal ({run.State}).");
        if (run.State == WorkflowState.HumanRequired || run.LastFailure?.HumanRequired == true)
            return new(RunHealth.HumanRequired, run.LastFailure?.RootCause ?? "Explicit human action is required.", "Human action required");
        if (run.State == WorkflowState.WaitingRetry)
        {
            if (run.ResumeStage is null || !IsResumableStage(run.ResumeStage.Value))
                return new(RunHealth.Orphaned, "WAITING_RETRY has no valid persisted ResumeStage.");
            return new(RunHealth.WaitingRetry, "Retry is persisted and resumable.", $"Resume {run.ResumeStage}");
        }

        return run.State switch
        {
            WorkflowState.Created => Active("Route task", "Created maps to routing execution."),
            WorkflowState.Routing => Active("Execute routing", "Routing is in progress and can be resumed."),
            WorkflowState.Routed when Orchestrator.IsReferenceValidationOnly(run.Task)
                => new(RunHealth.Orphaned, "Read-only reference validation reached Routed; it has no implementation continuation."),
            WorkflowState.Routed => Active("Start implementation", "Routed normally maps to implementation."),
            WorkflowState.Implementing when run.ImplementationCompleted => Active("Run validation", "Implementation is complete and validation is pending."),
            WorkflowState.Implementing => Active("Continue implementation", "Implementation is in progress and can be resumed."),
            WorkflowState.Validating when run.ValidationCompleted => Active("Run review", "Validation is complete and review is pending."),
            WorkflowState.Validating => Active("Run validation", "Validation is in progress and can be resumed."),
            WorkflowState.Reviewing when run.ReviewCompleted && run.Reviews.LastOrDefault()?.Decision == ReviewDecision.Fail
                => Active("Start remediation", "The persisted review failed and remediation is pending."),
            WorkflowState.Reviewing when run.ReviewCompleted && run.Reviews.LastOrDefault()?.Decision == ReviewDecision.Pass
                => Active("Finalize approval", "A passing review is persisted and approval is pending."),
            WorkflowState.Reviewing => Active("Run review", "Review is in progress and can be resumed."),
            WorkflowState.Remediating when run.RemediationCompleted => Active("Run validation", "Remediation is complete and validation is pending."),
            WorkflowState.Remediating => Active("Continue remediation", "Remediation is in progress and can be resumed."),
            _ => new(RunHealth.Orphaned, $"State {run.State} has no valid continuation mapping.")
        };
    }

    private static RunHealthAssessment Active(string action, string reason) => new(RunHealth.ValidActive, reason, action);
    private static bool IsResumableStage(WorkflowState stage) => stage is WorkflowState.Routing or WorkflowState.Implementing or WorkflowState.Validating or WorkflowState.Reviewing or WorkflowState.Remediating;
}
