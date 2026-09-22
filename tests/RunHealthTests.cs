using OnlineOs.AiOrchestrator.Infrastructure;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Pipeline;

namespace OnlineOs.AiOrchestrator.Tests;

public sealed class RunHealthTests
{
    [Fact]
    public void RoutedImplementationRunIsResumableWithAnAction()
    {
        var run = TestData.Run();
        run.State = WorkflowState.Routed;
        var assessment = RunHealthEvaluator.Assess(run);
        Assert.Equal(RunHealth.ValidActive, assessment.Health);
        Assert.Equal("Start implementation", assessment.NextAction);
    }

    [Fact]
    public void RoutedReferenceValidationRunIsOrphaned()
    {
        var run = TestData.Run();
        run.Task = new DevelopmentTask("M3-REFERENCE-VALIDATION", "Read-only OnlineOS reference validation", "Validate reference without implementation.");
        run.State = WorkflowState.Routed;
        var assessment = RunHealthEvaluator.Assess(run);
        Assert.Equal(RunHealth.Orphaned, assessment.Health);
        Assert.Contains("no implementation continuation", assessment.Reason);
    }

    [Fact]
    public void WaitingRetryRequiresAValidResumeStage()
    {
        var run = TestData.Run();
        run.State = WorkflowState.WaitingRetry;
        Assert.Equal(RunHealth.Orphaned, RunHealthEvaluator.Assess(run).Health);
        run.ResumeStage = WorkflowState.Validating;
        Assert.Equal(RunHealth.WaitingRetry, RunHealthEvaluator.Assess(run).Health);
    }

    [Fact]
    public void CrashRecoverableStagesRemainResumable()
    {
        var run = TestData.Run();
        run.State = WorkflowState.Implementing;
        Assert.True(RunHealthEvaluator.Assess(run).CanResume);
        run.ImplementationCompleted = true;
        Assert.Equal("Run validation", RunHealthEvaluator.Assess(run).NextAction);
    }

    [Fact]
    public async Task OrphanCanBeAbandonedWithoutDeletingArtifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), $"onlineos-health-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var store = new RunStore(root, ".ai-runs");
            var run = TestData.Run();
            run.State = WorkflowState.Routed;
            run.Task = new DevelopmentTask("M3-REFERENCE-VALIDATION", "Read-only OnlineOS reference validation", "Validate reference without implementation.");
            await store.InitializeAsync(run);
            await store.SaveArtifactAsync(run.RunId, "audit.json", new { preserved = true });
            await store.AbandonAsync(run.RunId, "stale read-only validation run");
            var persisted = await store.LoadAsync(run.RunId);
            Assert.Equal(WorkflowState.Abandoned, persisted!.State);
            Assert.Equal("stale read-only validation run", persisted.AbandonmentReason);
            Assert.NotNull(persisted.AbandonedAt);
            Assert.False(File.Exists(Path.Combine(root, ".ai-runs", "active-run.json")));
            Assert.True(File.Exists(Path.Combine(root, ".ai-runs", run.RunId, "audit.json")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task AbandonRejectsResumableRun()
    {
        var root = Path.Combine(Path.GetTempPath(), $"onlineos-health-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var store = new RunStore(root, ".ai-runs");
            var run = TestData.Run();
            await store.InitializeAsync(run);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.AbandonAsync(run.RunId, "must not abandon"));
            Assert.Equal(WorkflowState.Created, (await store.LoadAsync(run.RunId))!.State);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task TerminalPointerIsRepairedAndHistoryRemains()
    {
        var root = Path.Combine(Path.GetTempPath(), $"onlineos-health-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var store = new RunStore(root, ".ai-runs");
            var run = TestData.Run();
            await store.InitializeAsync(run);
            run.State = WorkflowState.Completed;
            await store.SaveAsync(run);
            var pointer = Path.Combine(root, ".ai-runs", "active-run.json");
            await File.WriteAllTextAsync(pointer, "{\"runId\":\"" + run.RunId + "\"}");
            var health = await store.AssessActiveAsync();
            Assert.Equal(RunHealth.Terminal, health.Health);
            Assert.True(File.Exists(Path.Combine(root, ".ai-runs", run.RunId, "run.json")));
            Assert.False(File.Exists(Path.Combine(root, ".ai-runs", "active-run.json")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
