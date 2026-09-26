namespace IAEngine.Core.Git;

public enum GitCheckpointScope
{
    Task,
    Milestone
}

public enum GitCheckpointState
{
    Approved,
    CompleteAwaitingApproval,
    HumanRequired,
    Failed,
    RemediationRequired,
    Cancelled
}

public enum GitCheckpointDecisionStatus
{
    Allowed,
    Blocked,
    HumanRequired,
    Invalid
}

public sealed record GitCheckpointRequest(
    string ProjectId,
    string WorkspaceRoot,
    GitCheckpointScope Scope,
    string? MilestoneId,
    string TaskId,
    string CurrentBranch,
    string ProposedCommitMessage,
    GitCheckpointState State,
    bool ValidationPassed,
    bool ReviewPassed,
    bool RemediationCompleted,
    bool TaskCompleted,
    bool MilestoneCompleted,
    bool HumanApprovalRequired,
    bool HumanApprovalProvided,
    bool BranchAuthorized,
    bool HasChanges,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<string> ExpectedFiles,
    bool DiffCheckPassed,
    bool WorkspaceExpected,
    bool HasConflicts,
    bool SecretsDetected);

public sealed record GitCheckpointDecision(
    GitCheckpointDecisionStatus Status,
    string Reason,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> FilesEvaluated,
    string Branch,
    string ProposedCommitMessage,
    bool CommitAllowed,
    bool RequiresHumanApproval)
{
    public static GitCheckpointDecision Invalid(string reason, GitCheckpointRequest request, params string[] evidence)
        => new(GitCheckpointDecisionStatus.Invalid, reason, evidence, request.ChangedFiles ?? [], request.CurrentBranch, request.ProposedCommitMessage, false, false);
}

public sealed record GitCheckpointResult(
    bool Succeeded,
    string? CommitSha,
    string Message,
    string Branch,
    IReadOnlyList<string> FilesIncluded,
    DateTimeOffset Timestamp,
    string? FailureReason,
    bool WorkspaceChanged,
    bool PushPerformed,
    bool MergePerformed)
{
    public static GitCheckpointResult Blocked(string reason, GitCheckpointRequest request)
        => new(false, null, request.ProposedCommitMessage, request.CurrentBranch, request.ChangedFiles ?? [], DateTimeOffset.UtcNow, reason, request.HasChanges, false, false);
}

public sealed record GitCheckpointExecutionResult(
    GitCheckpointDecision Decision,
    GitCheckpointResult? Result);

public interface IGitCheckpointCoordinator
{
    Task<GitCheckpointDecision> EvaluateAsync(
        GitCheckpointRequest request,
        CancellationToken cancellationToken = default);

    Task<GitCheckpointResult> CommitAsync(
        GitCheckpointRequest request,
        CancellationToken cancellationToken = default);
}

public static class GitCheckpointPolicy
{
    public static GitCheckpointDecision Evaluate(GitCheckpointRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var evidence = new List<string>();

        if (string.IsNullOrWhiteSpace(request.ProjectId) || string.IsNullOrWhiteSpace(request.WorkspaceRoot)
            || string.IsNullOrWhiteSpace(request.CurrentBranch) || string.IsNullOrWhiteSpace(request.TaskId))
            return GitCheckpointDecision.Invalid("Checkpoint request is missing required identity fields.", request, "project", "workspace", "branch", "task");

        if (!Directory.Exists(request.WorkspaceRoot))
            return GitCheckpointDecision.Invalid("Checkpoint workspace does not exist.", request, request.WorkspaceRoot);

        if (request.State == GitCheckpointState.HumanRequired || request.HumanApprovalRequired && !request.HumanApprovalProvided)
            return Decision(GitCheckpointDecisionStatus.HumanRequired, "Human approval is required before a checkpoint can be committed.", request, evidence, true);

        if (request.State is GitCheckpointState.Failed or GitCheckpointState.RemediationRequired or GitCheckpointState.Cancelled)
            return Decision(GitCheckpointDecisionStatus.Blocked, $"Checkpoint state is {request.State}; commit is not allowed.", request, evidence, false);

        if (request.Scope == GitCheckpointScope.Milestone && request.State == GitCheckpointState.CompleteAwaitingApproval)
            return Decision(GitCheckpointDecisionStatus.Blocked, "A milestone awaiting checkpoint approval cannot commit yet.", request, ["milestone-awaiting-approval"], false);

        var blockers = new List<string>();
        var changedFiles = request.ChangedFiles ?? [];
        var expectedFiles = request.ExpectedFiles ?? [];
        if (!request.ValidationPassed) blockers.Add("validation-failed");
        if (!request.ReviewPassed) blockers.Add("review-failed");
        if (!request.RemediationCompleted) blockers.Add("remediation-incomplete");
        if (!request.TaskCompleted) blockers.Add("task-incomplete");
        if (request.Scope == GitCheckpointScope.Milestone && !request.MilestoneCompleted) blockers.Add("milestone-incomplete");
        if (!request.BranchAuthorized) blockers.Add("branch-not-authorized");
        if (!request.HasChanges || changedFiles.Count == 0) blockers.Add("no-changes");
        if (expectedFiles.Count == 0) blockers.Add("expected-files-not-declared");
        else if (changedFiles.Except(expectedFiles, StringComparer.Ordinal).Any()) blockers.Add("unrelated-changes");
        if (string.IsNullOrWhiteSpace(request.ProposedCommitMessage) || !IsSemanticCommitMessage(request.ProposedCommitMessage)) blockers.Add("invalid-semantic-message");
        if (!request.DiffCheckPassed) blockers.Add("diff-check-failed");
        if (!request.WorkspaceExpected) blockers.Add("workspace-state-unexpected");
        if (request.HasConflicts) blockers.Add("merge-conflicts-present");
        if (request.SecretsDetected) blockers.Add("secrets-detected");

        evidence.AddRange(blockers);
        return blockers.Count > 0
            ? Decision(GitCheckpointDecisionStatus.Blocked, $"Checkpoint is blocked by: {string.Join(", ", blockers)}.", request, evidence, false)
            : Decision(GitCheckpointDecisionStatus.Allowed, "Checkpoint policy passed; commit may be requested.", request, ["validation", "review", "remediation", "diff", "branch", "workspace"], false);
    }

    public static bool IsSemanticCommitMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        var separator = message.IndexOf(": ", StringComparison.Ordinal);
        if (separator <= 0 || separator == message.Length - 2) return false;
        var type = message[..separator].Split('(', 2)[0].TrimEnd('!');
        return type is "feat" or "fix" or "test" or "docs" or "refactor" or "perf" or "build" or "ci" or "chore" or "revert";
    }

    private static GitCheckpointDecision Decision(
        GitCheckpointDecisionStatus status,
        string reason,
        GitCheckpointRequest request,
        IReadOnlyList<string> evidence,
        bool requiresHumanApproval)
        => new(status, reason, evidence, request.ChangedFiles ?? [], request.CurrentBranch, request.ProposedCommitMessage, status == GitCheckpointDecisionStatus.Allowed, requiresHumanApproval);
}
