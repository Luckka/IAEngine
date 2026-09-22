using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Pipeline;

namespace OnlineOs.AiOrchestrator.Tests;

public sealed class RunStateConsistencyTests
{
    [Fact]
    public void ApprovedClearsCurrentFailureAndRecoveryButPreservesAudit()
    {
        var run = TestData.Run();
        var diagnosis = TestData.Failure(FailureCategory.PermissionDenied, humanRequired: true);
        run.LastFailure = diagnosis;
        run.Diagnoses.Add(diagnosis);
        run.Recoveries.Add(new RecoveryRecord(RecoveryAction.RemediateReview, 1, 0, "remediated", DateTimeOffset.UtcNow));
        run.ActiveRecovery = run.Recoveries[0];
        run.ResumeStage = WorkflowState.Reviewing;
        run.ResumeAfter = DateTimeOffset.UtcNow.AddMinutes(1);
        run.RemediationCount = 1;

        Move(run, WorkflowState.Approved);

        Assert.Equal(WorkflowState.Approved, run.State);
        Assert.Equal(WorkflowState.Approved, run.CurrentStage);
        Assert.Equal("PASS", run.FinalDecision);
        Assert.Null(run.LastFailure);
        Assert.Null(run.ActiveRecovery);
        Assert.Null(run.ResumeStage);
        Assert.Null(run.ResumeAfter);
        Assert.Single(run.Diagnoses);
        Assert.Single(run.Recoveries);
        Assert.Equal(1, run.RemediationCount);
    }

    [Fact]
    public void ApprovedNormalizesContradictoryDecisionAndHumanMetadata()
    {
        var run = TestData.Run();
        run.FinalDecision = "HUMAN_REQUIRED";
        run.LastFailure = TestData.Failure(FailureCategory.PermissionDenied, humanRequired: true);

        Move(run, WorkflowState.Approved);

        Assert.Equal("PASS", run.FinalDecision);
        Assert.Null(run.LastFailure);
    }

    [Fact]
    public void HumanRequiredRetainsActionableFailureAndCannotRemainPass()
    {
        var run = TestData.Run();
        run.FinalDecision = "PASS";
        run.LastFailure = TestData.Failure(FailureCategory.PermissionDenied, humanRequired: true);

        Move(run, WorkflowState.HumanRequired);

        Assert.Equal("HUMAN_REQUIRED", run.FinalDecision);
        Assert.True(run.LastFailure.HumanRequired);
        Assert.Equal(FailureCategory.PermissionDenied, run.LastFailure.Category);
        Assert.Equal(WorkflowState.HumanRequired, run.CurrentStage);
    }

    [Fact]
    public void HumanRequiredNormalizesTransientExhaustionReporting()
    {
        var run = TestData.Run();
        run.LastFailure = TestData.Failure(FailureCategory.ProviderRateLimit);

        Move(run, WorkflowState.HumanRequired);

        Assert.True(run.LastFailure!.HumanRequired);
        Assert.False(run.LastFailure.Recoverable);
        Assert.Equal(RecoveryAction.HumanRequired, run.LastFailure.RecommendedAction);
    }

    [Fact]
    public void FailedRetainsFailureAndCannotRemainPass()
    {
        var run = TestData.Run();
        run.FinalDecision = "PASS";
        run.LastFailure = TestData.Failure(FailureCategory.ValidationFailure);

        Move(run, WorkflowState.Failed);

        Assert.Equal("FAILED", run.FinalDecision);
        Assert.NotNull(run.LastFailure);
        Assert.Equal(WorkflowState.Failed, run.CurrentStage);
    }

    [Fact]
    public void WaitingRetryRejectsMissingRecoveryMetadata()
    {
        var run = TestData.Run();

        Assert.Throws<InvalidOperationException>(() => Move(run, WorkflowState.WaitingRetry));
    }

    [Fact]
    public void WaitingRetryRetainsRecoveryAndResumeMetadata()
    {
        var run = TestData.Run();
        run.ActiveRecovery = new RecoveryRecord(RecoveryAction.RetryAfterBackoff, 1, 5, "retry", DateTimeOffset.UtcNow);
        run.ResumeStage = WorkflowState.Reviewing;
        run.ResumeAfter = DateTimeOffset.UtcNow.AddMinutes(1);

        Move(run, WorkflowState.WaitingRetry);

        Assert.Equal(WorkflowState.WaitingRetry, run.CurrentStage);
        Assert.NotNull(run.ActiveRecovery);
        Assert.Equal(WorkflowState.Reviewing, run.ResumeStage);
        Assert.NotNull(run.ResumeAfter);
    }

    private static void Move(RunRecord run, WorkflowState next)
    {
        var machine = new WorkflowStateMachine();
        foreach (var state in new[] { WorkflowState.Routing, WorkflowState.Routed, WorkflowState.Implementing, WorkflowState.Validating, WorkflowState.Reviewing })
        {
            if (run.State == state) break;
            machine.Move(run, state, "test");
        }
        machine.Move(run, next, "test");
    }
}
