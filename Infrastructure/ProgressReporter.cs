using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Models;

namespace OnlineOs.AiOrchestrator.Infrastructure;

public sealed class ConsoleProgressReporter(
    TextWriter output,
    TimeProvider? timeProvider = null,
    TimeSpan? heartbeatInterval = null) : IProgressReporter
{
    private readonly object gate = new();
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan heartbeatEvery = heartbeatInterval ?? TimeSpan.FromSeconds(30);
    private long startedAt;
    private CancellationTokenSource? heartbeatCancellation;
    private string? lastFailureReason;

    public void PrintHeader(string runId, string taskTitle, string branch)
    {
        startedAt = clock.GetTimestamp();
        WriteLine("----------------------------------------");
        WriteLine($"Run: {runId}");
        WriteLine($"Task: {taskTitle}");
        WriteLine($"Branch: {branch}");
        WriteLine("----------------------------------------");
    }

    public void StartStage(string stage, string? heartbeatMessage = null)
    {
        StopHeartbeat();
        lastFailureReason = null;
        WriteLine($"[{Elapsed()}] ● {stage}...");
        var cancellation = new CancellationTokenSource();
        heartbeatCancellation = cancellation;
        _ = HeartbeatAsync(heartbeatMessage ?? $"{stage} still running", cancellation.Token);
    }

    public void CompleteStage(string summary, bool succeeded = true)
    {
        StopHeartbeat();
        WriteLine($"[{Elapsed()}] {(succeeded ? "✓" : "✗")} {summary}");
        if (!succeeded) lastFailureReason = summary;
    }

    public void PrintFinal(RunRecord run)
    {
        StopHeartbeat();
        var heading = run.State switch
        {
            WorkflowState.Approved => "WORKFLOW APPROVED",
            WorkflowState.HumanRequired => "HUMAN REVIEW REQUIRED",
            _ => "WORKFLOW FAILED"
        };
        var latestValidation = run.LatestValidationResults.Count > 0 ? run.LatestValidationResults : run.ValidationResults;
        var validation = latestValidation.Count == 0 ? "NOT RUN"
            : latestValidation.Any(x => x.Required && !x.Passed) ? "FAIL" : "PASS";
        var review = run.Reviews.Count == 0 ? "NOT RUN" : run.Reviews[^1].Decision.ToString().ToUpperInvariant();
        WriteLine("----------------------------------------");
        WriteLine(heading);
        WriteLine($"Run: {run.RunId}");
        WriteLine($"Duration: {Elapsed()}");
        WriteLine($"State: {run.State.ToString().ToUpperInvariant()}");
        WriteLine($"Decision: {run.FinalDecision ?? "NONE"}");
        WriteLine($"Validation: {validation}");
        WriteLine($"Review: {review}");
        WriteLine($"Remediations: {run.RemediationCount}");
        var printedHumanRequired = false;
        if (run.LastFailure is not null)
        {
            WriteLine($"Stage: {run.LastFailure.Stage}");
            WriteLine($"Failure: {run.LastFailure.Category}");
            WriteLine($"Root cause: {run.LastFailure.RootCause}");
            WriteLine($"Human required: {(run.LastFailure.HumanRequired ? "YES" : "NO")}");
            printedHumanRequired = true;
        }
        else if (run.State == WorkflowState.Failed)
            WriteLine($"Reason: {lastFailureReason ?? "Stage failed."}");
        if (!printedHumanRequired) WriteLine($"Human required: {(run.State == WorkflowState.HumanRequired ? "YES" : "NO")}");
        if (run.ResumeAfter is not null) WriteLine($"Resume after: {run.ResumeAfter:O}");
        WriteLine("----------------------------------------");
    }

    public void PrintFailure(string runId, string stage, string reason)
    {
        StopHeartbeat();
        WriteLine("----------------------------------------");
        WriteLine("WORKFLOW FAILED");
        WriteLine($"Run: {runId}");
        WriteLine($"Duration: {Elapsed()}");
        WriteLine($"Stage: {stage}");
        WriteLine($"Reason: {reason}");
        WriteLine("----------------------------------------");
    }

    public void Dispose() => StopHeartbeat();

    private async Task HeartbeatAsync(string message, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                await Task.Delay(heartbeatEvery, clock, ct);
                WriteLine($"[{Elapsed()}]   {message}...");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private void StopHeartbeat()
    {
        heartbeatCancellation?.Cancel();
        heartbeatCancellation?.Dispose();
        heartbeatCancellation = null;
    }

    private string Elapsed()
    {
        var elapsed = clock.GetElapsedTime(startedAt);
        return elapsed.TotalHours >= 1 ? elapsed.ToString(@"hh\:mm\:ss") : elapsed.ToString(@"mm\:ss");
    }

    private void WriteLine(string value)
    {
        lock (gate)
        {
            output.WriteLine(value);
            output.Flush();
        }
    }
}

public sealed class NullProgressReporter : IProgressReporter
{
    public static NullProgressReporter Instance { get; } = new();
    public void PrintHeader(string runId, string taskTitle, string branch) { }
    public void StartStage(string stage, string? heartbeatMessage = null) { }
    public void CompleteStage(string summary, bool succeeded = true) { }
    public void PrintFinal(RunRecord run) { }
    public void PrintFailure(string runId, string stage, string reason) { }
    public void Dispose() { }
}
