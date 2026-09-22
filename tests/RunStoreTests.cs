using OnlineOs.AiOrchestrator.Infrastructure;
using OnlineOs.AiOrchestrator.Models;

namespace OnlineOs.AiOrchestrator.Tests;

public sealed class RunStoreTests
{
    [Fact]
    public async Task SaveNormalizesApprovedStateBeforePersisting()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"onlineos-runstore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var run = TestData.Run();
            run.State = WorkflowState.Approved;
            run.CurrentStage = WorkflowState.Reviewing;
            run.FinalDecision = "HUMAN_REQUIRED";
            run.LastFailure = TestData.Failure(FailureCategory.PermissionDenied, humanRequired: true);
            run.ResumeStage = WorkflowState.Reviewing;
            run.ResumeAfter = DateTimeOffset.UtcNow.AddMinutes(1);

            var store = new RunStore(directory, ".ai-runs");
            await store.SaveAsync(run);
            var persisted = await store.LoadAsync(run.RunId);

            Assert.NotNull(persisted);
            Assert.Equal(WorkflowState.Approved, persisted.State);
            Assert.Equal("PASS", persisted.FinalDecision);
            Assert.Equal(WorkflowState.Approved, persisted.CurrentStage);
            Assert.Null(persisted.LastFailure);
            Assert.Null(persisted.ResumeStage);
            Assert.Null(persisted.ResumeAfter);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
