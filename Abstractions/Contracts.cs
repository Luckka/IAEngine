using OnlineOs.AiOrchestrator.Models;

namespace OnlineOs.AiOrchestrator.Abstractions;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken = default);
    Task<ProcessResult> StartDetachedAsync(ProcessSpec spec, CancellationToken cancellationToken = default)
        => Task.FromResult(new ProcessResult(spec.FileName, -1, "", "Detached process start is not supported by this runner.", TimeSpan.Zero));
}
public interface ITaskRouter
{
    Task<RoutingResult> RouteAsync(DevelopmentTask task, CancellationToken cancellationToken = default);
    Task<(bool Reachable, bool ModelAvailable, string Detail)> CheckHealthAsync(CancellationToken cancellationToken = default);
}
public interface IFailureDiagnoser
{
    Task<DiagnosisSuggestion?> DiagnoseAsync(FailureContext context, CancellationToken cancellationToken = default);
}
public interface IImplementationAgent
{
    Task<ImplementationResult> ImplementAsync(DevelopmentTask task, RoutingResult route, EngineeringProfile engineering, CancellationToken cancellationToken = default);
    Task<ImplementationResult> RemediateAsync(DevelopmentTask task, EngineeringProfile engineering, IReadOnlyList<ValidationResult> validation, ReviewResult? review, CancellationToken cancellationToken = default, int contextReductionAttempt = 0);
}
public interface IReviewAgent
{
    Task<ReviewResult> ReviewAsync(DevelopmentTask task, EngineeringProfile engineering, string gitDiff, CancellationToken cancellationToken = default);
    Task<ReviewResult> ReviewAsync(DevelopmentTask task, EngineeringProfile engineering, IReadOnlyList<ValidationResult> validation, string gitDiff, CancellationToken cancellationToken = default)
        => ReviewAsync(task, engineering, gitDiff, cancellationToken);
}
public interface IValidationRunner
{
    Task<IReadOnlyList<ValidationResult>> RunAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ValidationResult>> RunAsync(EngineeringProfile engineering, CancellationToken cancellationToken = default)
        => RunAsync(cancellationToken);
}
public interface IGitService
{
    Task<string> GetRootAsync(CancellationToken cancellationToken = default);
    Task<string> GetBranchAsync(CancellationToken cancellationToken = default);
    Task<string> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<string> GetDiffAsync(CancellationToken cancellationToken = default);
    Task<string> GetCommitAsync(CancellationToken cancellationToken = default);
    Task<(bool Safe, string Reason)> CheckBranchSafetyAsync(CancellationToken cancellationToken = default);
    async Task<bool> IsOnBranchAsync(string expectedBranch, CancellationToken cancellationToken = default)
        => string.Equals(await GetBranchAsync(cancellationToken), expectedBranch, StringComparison.Ordinal);
}
public interface IRunStore
{
    Task InitializeAsync(RunRecord run, CancellationToken cancellationToken = default);
    Task SaveAsync(RunRecord run, CancellationToken cancellationToken = default);
    Task SaveArtifactAsync<T>(string runId, string name, T value, CancellationToken cancellationToken = default);
    Task<T?> LoadLatestArtifactAsync<T>(string runId, string prefix, CancellationToken cancellationToken = default) => Task.FromResult<T?>(default);
    Task<T?> LoadArtifactAsync<T>(string runId, string name, CancellationToken cancellationToken = default) => Task.FromResult<T?>(default);
    Task<RunRecord?> LoadAsync(string runId, CancellationToken cancellationToken = default) => Task.FromResult<RunRecord?>(null);
    Task<RunRecord?> FindActiveAsync(CancellationToken cancellationToken = default) => Task.FromResult<RunRecord?>(null);
    Task<RunHealthAssessment> AssessActiveAsync(CancellationToken cancellationToken = default) => Task.FromResult(new RunHealthAssessment(RunHealth.Terminal, "No active run exists."));
    Task AbandonAsync(string runId, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException("This run store does not support administrative abandonment.");
}
public interface IProgressReporter : IDisposable
{
    void PrintHeader(string runId, string taskTitle, string branch);
    void StartStage(string stage, string? heartbeatMessage = null);
    void CompleteStage(string summary, bool succeeded = true);
    void PrintFinal(RunRecord run);
    void PrintFailure(string runId, string stage, string reason);
}
