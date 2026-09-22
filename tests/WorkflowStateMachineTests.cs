using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Pipeline;

namespace OnlineOs.AiOrchestrator.Tests;

public sealed class WorkflowStateMachineTests
{
    [Fact]
    public void AllowsExpectedSuccessfulPath()
    {
        var run = TestData.Run();
        var machine = new WorkflowStateMachine();
        foreach (var state in new[] { WorkflowState.Routing, WorkflowState.Routed, WorkflowState.Implementing, WorkflowState.Validating, WorkflowState.Reviewing, WorkflowState.Approved })
            machine.Move(run, state, "test");
        Assert.Equal(WorkflowState.Approved, run.State);
        Assert.Equal(6, run.Transitions.Count);
    }

    [Fact]
    public void RejectsInvalidTransition()
    {
        var run = TestData.Run();
        Assert.Throws<InvalidOperationException>(() => new WorkflowStateMachine().Move(run, WorkflowState.Approved, "invalid"));
    }
}
