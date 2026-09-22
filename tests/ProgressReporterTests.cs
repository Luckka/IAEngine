using OnlineOs.AiOrchestrator.Infrastructure;
using OnlineOs.AiOrchestrator.Models;

namespace OnlineOs.AiOrchestrator.Tests;

public sealed class ProgressReporterTests
{
    [Fact]
    public void PrintsHeaderStageCompletionAndFlushesImmediately()
    {
        var output = new TrackingWriter();
        using var reporter = new ConsoleProgressReporter(output);

        reporter.PrintHeader("RUN-123", "Test task", "feature/test");
        reporter.StartStage("Claude implementing", "Claude still working");
        reporter.CompleteStage("Claude completed");

        var text = output.ToString();
        Assert.Contains("Run: RUN-123", text);
        Assert.Contains("Task: Test task", text);
        Assert.Contains("Branch: feature/test", text);
        Assert.Contains("● Claude implementing...", text);
        Assert.Contains("✓ Claude completed", text);
        Assert.True(output.FlushCount >= 7);
    }

    [Fact]
    public void PrintsSafeFinalSummary()
    {
        var output = new TrackingWriter();
        using var reporter = new ConsoleProgressReporter(output);
        var run = TestData.Run();
        run.State = WorkflowState.Approved;
        run.FinalDecision = "PASS";
        run.ValidationResults.Add(TestData.Validation(true));
        run.Reviews.Add(new ReviewResult(ReviewDecision.Pass, [], "reviewed"));

        reporter.PrintHeader(run.RunId, run.Task.Title, "feature/test");
        reporter.PrintFinal(run);

        var text = output.ToString();
        Assert.Contains("WORKFLOW APPROVED", text);
        Assert.DoesNotContain("READY FOR HUMAN REVIEW", text);
        Assert.Contains("Decision: PASS", text);
        Assert.Contains("Human required: NO", text);
        Assert.Contains("Validation: PASS", text);
        Assert.Contains("Review: PASS", text);
        Assert.Contains("Remediations: 0", text);
    }

    [Fact]
    public void FinalSummaryUsesLatestValidationCycle()
    {
        var output = new TrackingWriter();
        using var reporter = new ConsoleProgressReporter(output);
        var run = TestData.Run();
        run.State = WorkflowState.Approved;
        run.ValidationResults.Add(TestData.Validation(false));
        run.LatestValidationResults.Add(TestData.Validation(true));

        reporter.PrintHeader(run.RunId, run.Task.Title, "feature/test");
        reporter.PrintFinal(run);

        Assert.Contains("Validation: PASS", output.ToString());
    }

    [Fact]
    public async Task PrintsHeartbeatForLongRunningStageWithoutProviderOutput()
    {
        var output = new TrackingWriter();
        using var reporter = new ConsoleProgressReporter(output, heartbeatInterval: TimeSpan.FromMilliseconds(10));
        reporter.PrintHeader("RUN-123", "Test task", "feature/test");

        reporter.StartStage("Claude implementing", "Claude still working");
        await Task.Delay(100);
        reporter.CompleteStage("Claude completed");

        Assert.Contains("Claude still working...", output.ToString());
    }

    private sealed class TrackingWriter : StringWriter
    {
        public int FlushCount { get; private set; }

        public override void Flush()
        {
            FlushCount++;
            base.Flush();
        }
    }
}
