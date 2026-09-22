using OnlineOs.AiOrchestrator.Models;

namespace OnlineOs.AiOrchestrator.Pipeline;

public sealed class WorkflowStateMachine
{
    private static readonly IReadOnlyDictionary<WorkflowState, WorkflowState[]> Allowed = new Dictionary<WorkflowState, WorkflowState[]>
    {
        [WorkflowState.Created] = [WorkflowState.Routing, WorkflowState.Completed, WorkflowState.Failed],
        [WorkflowState.Routing] = [WorkflowState.Routed, WorkflowState.WaitingRetry, WorkflowState.Failed],
        [WorkflowState.Routed] = [WorkflowState.Implementing, WorkflowState.Completed, WorkflowState.WaitingRetry, WorkflowState.Failed],
        [WorkflowState.Implementing] = [WorkflowState.Validating, WorkflowState.WaitingRetry, WorkflowState.HumanRequired, WorkflowState.Failed],
        [WorkflowState.Validating] = [WorkflowState.Reviewing, WorkflowState.Remediating, WorkflowState.WaitingRetry, WorkflowState.HumanRequired, WorkflowState.Failed],
        [WorkflowState.Reviewing] = [WorkflowState.Approved, WorkflowState.Remediating, WorkflowState.WaitingRetry, WorkflowState.HumanRequired, WorkflowState.Failed],
        [WorkflowState.Remediating] = [WorkflowState.Validating, WorkflowState.WaitingRetry, WorkflowState.HumanRequired, WorkflowState.Failed],
        [WorkflowState.WaitingRetry] = [WorkflowState.Routing, WorkflowState.Implementing, WorkflowState.Reviewing, WorkflowState.Remediating, WorkflowState.Validating, WorkflowState.HumanRequired, WorkflowState.Failed],
        [WorkflowState.Approved] = [],
        [WorkflowState.HumanRequired] = [],
        [WorkflowState.Failed] = [],
        [WorkflowState.Completed] = [],
        [WorkflowState.Abandoned] = []
    };

    public StateTransition Move(RunRecord run, WorkflowState next, string reason)
    {
        if (!Allowed[run.State].Contains(next)) throw new InvalidOperationException($"Invalid workflow transition: {run.State} -> {next}");
        var transition = new StateTransition(run.State, next, DateTimeOffset.UtcNow, reason);
        var previousStage = run.CurrentStage;
        run.State = next;
        run.Transitions.Add(transition);
        try
        {
            run.NormalizeForState();
        }
        catch
        {
            run.State = transition.From;
            run.CurrentStage = previousStage;
            run.Transitions.RemoveAt(run.Transitions.Count - 1);
            throw;
        }
        return transition;
    }

    public StateTransition ReopenForAuthorizedReviewRecovery(RunRecord run, string reason)
    {
        if (run.State != WorkflowState.HumanRequired)
            throw new InvalidOperationException("Only HumanRequired runs can be reopened by recovery authorization.");
        if (run.LastFailure is not { Category: FailureCategory.ReviewFailure, Stage: WorkflowState.Reviewing })
            throw new InvalidOperationException("Only HumanRequired review-failure runs are eligible for authorized recovery.");
        return Reopen(run, reason);
    }

    public StateTransition ReopenForAuthorizedTransientProviderRecovery(RunRecord run, string reason)
    {
        if (run.State != WorkflowState.HumanRequired)
            throw new InvalidOperationException("Only HumanRequired runs can be reopened by recovery authorization.");
        if (run.LastFailure is not { Stage: var stage } failure || !IsTransientProviderFailure(stage, failure.Category))
            throw new InvalidOperationException("Only HumanRequired whitelisted transient provider failures are eligible for authorized recovery.");
        return Reopen(run, reason);
    }

    public StateTransition ReopenForContextOverflowRecovery(RunRecord run, string reason)
    {
        if (run.State != WorkflowState.HumanRequired)
            throw new InvalidOperationException("Only HumanRequired runs can be reopened for context-overflow recovery.");
        if (run.LastFailure is not { Stage: WorkflowState.Remediating, Category: FailureCategory.ContextOverflow })
            throw new InvalidOperationException("Only HumanRequired remediation context-overflow runs are eligible for recovery.");
        return Reopen(run, reason);
    }

    public StateTransition ReopenForRoutineEngineeringRecovery(RunRecord run, string reason)
    {
        if (run.State != WorkflowState.HumanRequired)
            throw new InvalidOperationException("Only HumanRequired runs can be reopened by routine engineering recovery.");
        if (!HasActionableEngineeringFailure(run))
            throw new InvalidOperationException("HumanRequired run has no actionable routine engineering failure.");
        return Reopen(run, reason);
    }

    private static bool HasActionableEngineeringFailure(RunRecord run)
    {
        if (run.LastFailure?.Category is FailureCategory.ProductAmbiguity or FailureCategory.ArchitectureDecisionRequired
            or FailureCategory.OpenQuestionBlocking or FailureCategory.WorkspaceBoundaryViolation or FailureCategory.PermissionDenied
            or FailureCategory.DirtyWorkingTree or FailureCategory.ProtectedGitOperation or FailureCategory.ReferenceRepositoryMutation
            or FailureCategory.SecurityDecisionRequired or FailureCategory.ExternalAuthorizationRequired)
            return false;
        return run.Reviews.LastOrDefault()?.Decision == ReviewDecision.Fail
            || run.LatestValidationResults.Any(x => x.Required && !x.Passed)
            || run.ValidationResults.Any(x => x.Required && !x.Passed);
    }

    private static StateTransition Reopen(RunRecord run, string reason)
    {
        var transition = new StateTransition(WorkflowState.HumanRequired, WorkflowState.Validating, DateTimeOffset.UtcNow, reason);
        run.State = WorkflowState.Validating;
        run.Transitions.Add(transition);
        run.LastFailure = null;
        try { run.NormalizeForState(); }
        catch
        {
            run.State = WorkflowState.HumanRequired;
            run.Transitions.RemoveAt(run.Transitions.Count - 1);
            throw;
        }
        return transition;
    }

    private static bool IsTransientProviderFailure(WorkflowState stage, FailureCategory category) => stage switch
    {
        WorkflowState.Reviewing => category is FailureCategory.ProviderRateLimit or FailureCategory.ProviderUnavailable or FailureCategory.ProviderTimeout or FailureCategory.ProviderMalformedOutput or FailureCategory.CodexUnavailable,
        WorkflowState.Remediating => category is FailureCategory.ProviderRateLimit or FailureCategory.ProviderUnavailable or FailureCategory.ProviderTimeout or FailureCategory.ProviderMalformedOutput or FailureCategory.ClaudeUnavailable,
        _ => false
    };
}
