using System.Text.Json.Serialization;

namespace IAEngine.Core.Recovery;

public sealed record ExecutionKey(
    string ProjectId,
    string MilestoneId,
    string TaskId,
    string ExecutionId)
{
    public string Value => string.Join("/", ProjectId, MilestoneId, TaskId, ExecutionId);

    public override string ToString() => Value;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RecoveryReason
{
    ValidationFailure,
    ProviderFailure,
    Timeout,
    Cancellation,
    Crash,
    Interrupted,
    HumanRequired
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RecoveryStatus
{
    Running,
    Failed,
    HumanRequired,
    CompleteAwaitingApproval,
    Approved,
    Rejected,
    Abandoned,
    Recovered,
    Completed
}

public sealed record RecoveryArtifactIdentity(string ArtifactKey, string Name, string ContentHash);
public sealed record RecoveryCheckpointIdentity(string CheckpointKey, string? CommitSha = null);
public sealed record RecoveryApproval(string ApprovalId, string ExecutionKey, string Reason, DateTimeOffset ApprovedAt);

public sealed record RecoveryAttempt(
    int Number,
    string ExecutionKey,
    string ProjectId,
    string MilestoneId,
    string TaskId,
    RecoveryStatus Status,
    RecoveryReason Reason,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string? SanitizedError,
    IReadOnlyList<RecoveryArtifactIdentity> Artifacts,
    RecoveryCheckpointIdentity? Checkpoint,
    bool ApprovalRequired);

public sealed record RecoveryExecution(
    string ExecutionKey,
    string ProjectId,
    string MilestoneId,
    string TaskId,
    RecoveryStatus Status,
    int CurrentAttempt,
    IReadOnlyList<RecoveryAttempt> Attempts,
    IReadOnlyList<RecoveryArtifactIdentity> Artifacts,
    IReadOnlyList<RecoveryCheckpointIdentity> Checkpoints,
    IReadOnlyList<RecoveryApproval> Approvals,
    int SchemaVersion = 1);

public interface IRecoveryStore
{
    Task<RecoveryExecution?> GetAsync(string executionKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RecoveryAttempt>> GetAttemptsAsync(string executionKey, CancellationToken cancellationToken = default);
    Task<RecoveryExecution> SaveAsync(RecoveryExecution execution, CancellationToken cancellationToken = default);
}

public interface IExecutionRecoveryService
{
    Task<RecoveryExecution> StartAsync(ExecutionKey key, CancellationToken cancellationToken = default);
    Task<RecoveryAttempt> StartAttemptAsync(ExecutionKey key, RecoveryReason reason, CancellationToken cancellationToken = default);
    Task<RecoveryExecution> RecordAttemptAsync(ExecutionKey key, RecoveryAttempt attempt, CancellationToken cancellationToken = default);
    Task<RecoveryExecution> RecordArtifactAsync(ExecutionKey key, RecoveryArtifactIdentity artifact, CancellationToken cancellationToken = default);
    Task<RecoveryExecution> RecordCheckpointAsync(ExecutionKey key, RecoveryCheckpointIdentity checkpoint, CancellationToken cancellationToken = default);
    Task<RecoveryExecution> ApproveHumanRequiredAsync(ExecutionKey key, string approvalId, string reason, CancellationToken cancellationToken = default);
    Task<RecoveryExecution> RecoverAsync(ExecutionKey key, RecoveryReason reason, CancellationToken cancellationToken = default);
    Task<RecoveryExecution?> GetAsync(ExecutionKey key, CancellationToken cancellationToken = default);
}
