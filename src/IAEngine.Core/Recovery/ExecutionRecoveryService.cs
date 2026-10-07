namespace IAEngine.Core.Recovery;

public sealed class ExecutionRecoveryService(IRecoveryStore store) : IExecutionRecoveryService
{
    private static readonly System.Text.RegularExpressions.Regex SensitiveText = new(
        "(?i)(token|password|secret|api[_-]?key|authorization|cookie|credential)\\s*[:=]\\s*[^,;\\s]+",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    public async Task<RecoveryExecution> StartAsync(ExecutionKey key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        return await GetAsync(key, cancellationToken) ?? await store.SaveAsync(NewExecution(key), cancellationToken);
    }

    public async Task<RecoveryAttempt> StartAttemptAsync(ExecutionKey key, RecoveryReason reason, CancellationToken cancellationToken = default)
    {
        var execution = await StartAsync(key, cancellationToken);
        if (execution.Status == RecoveryStatus.HumanRequired)
            throw new InvalidOperationException("Human approval is required before a recovery attempt can start.");
        var number = execution.Attempts.Count + 1;
        return new(number, execution.ExecutionKey, execution.ProjectId, execution.MilestoneId, execution.TaskId,
            RecoveryStatus.Running, reason, DateTimeOffset.UtcNow, null, null, [], null, false);
    }

    public async Task<RecoveryExecution> RecordAttemptAsync(ExecutionKey key, RecoveryAttempt attempt, CancellationToken cancellationToken = default)
    {
        var execution = await RequireAsync(key, cancellationToken);
        if (!string.Equals(attempt.ExecutionKey, execution.ExecutionKey, StringComparison.Ordinal))
            throw new InvalidOperationException("Recovery attempt belongs to a different execution key.");
        if (execution.Attempts.Any(existing => existing.Number == attempt.Number))
            return execution;
        if (attempt.Number != execution.Attempts.Count + 1)
            throw new InvalidOperationException("Recovery attempts must be appended in order.");
        var status = attempt.Status == RecoveryStatus.HumanRequired ? RecoveryStatus.HumanRequired : attempt.Status;
        attempt = attempt with { SanitizedError = Sanitize(attempt.SanitizedError) };
        return await store.SaveAsync(execution with
        {
            Status = status,
            CurrentAttempt = attempt.Number,
            Attempts = [.. execution.Attempts, attempt]
        }, cancellationToken);
    }

    public async Task<RecoveryExecution> RecordArtifactAsync(ExecutionKey key, RecoveryArtifactIdentity artifact, CancellationToken cancellationToken = default)
    {
        var execution = await RequireAsync(key, cancellationToken);
        if (execution.Artifacts.Any(existing => existing.ArtifactKey == artifact.ArtifactKey)) return execution;
        return await store.SaveAsync(execution with { Artifacts = [.. execution.Artifacts, artifact] }, cancellationToken);
    }

    public async Task<RecoveryExecution> RecordCheckpointAsync(ExecutionKey key, RecoveryCheckpointIdentity checkpoint, CancellationToken cancellationToken = default)
    {
        var execution = await RequireAsync(key, cancellationToken);
        if (execution.Checkpoints.Any(existing => existing.CheckpointKey == checkpoint.CheckpointKey)) return execution;
        return await store.SaveAsync(execution with { Checkpoints = [.. execution.Checkpoints, checkpoint] }, cancellationToken);
    }

    public async Task<RecoveryExecution> ApproveHumanRequiredAsync(ExecutionKey key, string approvalId, string reason, CancellationToken cancellationToken = default)
    {
        var execution = await RequireAsync(key, cancellationToken);
        if (execution.Approvals.Any(approval => approval.ApprovalId == approvalId))
            throw new InvalidOperationException("Approval has already been recorded for this execution.");
        if (execution.Status != RecoveryStatus.HumanRequired)
            throw new InvalidOperationException("Only HumanRequired executions can be approved.");
        var approval = new RecoveryApproval(approvalId, execution.ExecutionKey, reason, DateTimeOffset.UtcNow);
        return await store.SaveAsync(execution with
        {
            Status = RecoveryStatus.Approved,
            Approvals = [.. execution.Approvals, approval]
        }, cancellationToken);
    }

    public async Task<RecoveryExecution> RecoverAsync(ExecutionKey key, RecoveryReason reason, CancellationToken cancellationToken = default)
    {
        var execution = await RequireAsync(key, cancellationToken);
        if (execution.Status == RecoveryStatus.HumanRequired)
            throw new InvalidOperationException("Human approval is required before recovery.");
        if (execution.Status == RecoveryStatus.Recovered) return execution;
        if (execution.Status is RecoveryStatus.Completed or RecoveryStatus.Abandoned)
            throw new InvalidOperationException($"Execution is terminal: {execution.Status}.");
        var attempt = await StartAttemptAsync(key, reason, cancellationToken);
        return await RecordAttemptAsync(key, attempt with { Status = RecoveryStatus.Recovered, EndedAt = DateTimeOffset.UtcNow }, cancellationToken);
    }

    public Task<RecoveryExecution?> GetAsync(ExecutionKey key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        return store.GetAsync(key.Value, cancellationToken);
    }

    private async Task<RecoveryExecution> RequireAsync(ExecutionKey key, CancellationToken cancellationToken)
        => await GetAsync(key, cancellationToken) ?? throw new KeyNotFoundException($"Execution '{key}' was not found.");

    private static RecoveryExecution NewExecution(ExecutionKey key)
        => new(key.Value, key.ProjectId, key.MilestoneId, key.TaskId, RecoveryStatus.Failed, 0, [], [], [], []);

    private static void ValidateKey(ExecutionKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (string.IsNullOrWhiteSpace(key.ProjectId) || string.IsNullOrWhiteSpace(key.MilestoneId)
            || string.IsNullOrWhiteSpace(key.TaskId) || string.IsNullOrWhiteSpace(key.ExecutionId))
            throw new ArgumentException("Execution key requires project, milestone, task and execution identifiers.", nameof(key));
    }

    private static string? Sanitize(string? error)
        => string.IsNullOrWhiteSpace(error) ? error : SensitiveText.Replace(error, "$1=[REDACTED]");
}
