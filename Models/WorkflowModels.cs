using System.Text.Json.Serialization;
using IAEngine.Core.Git;

namespace OnlineOs.AiOrchestrator.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkflowState { Created, Routing, Routed, Implementing, Validating, Reviewing, Remediating, WaitingRetry, Approved, HumanRequired, Failed, Completed, Abandoned }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RunHealth { ValidActive, WaitingRetry, HumanRequired, Terminal, Orphaned, MissingRun, MalformedPointer }

public sealed record RunHealthAssessment(RunHealth Health, string Reason, string? NextAction = null)
{
    public bool IsTerminal => Health == RunHealth.Terminal;
    public bool CanResume => Health is RunHealth.ValidActive or RunHealth.WaitingRetry;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FailureCategory
{
    ProviderRateLimit, ProviderUnavailable, ProviderTimeout, ProviderMalformedOutput, ProviderStructuredRefusal,
    PermissionDenied, PreflightDependencyUnavailable, OllamaUnavailable, OllamaModelUnavailable, ClaudeUnavailable,
    CodexUnavailable, FlutterUnavailable, DotnetUnavailable, ValidationFailure, FormattingFailure, StaticAnalysisFailure,
    UnitTestFailure, ResponsiveTestFailure, IntegrationTestFailure, ReviewFailure, RemediationFailure, ContextOverflow,
    WorkspaceBoundaryViolation, DirtyWorkingTree, GitStateFailure, ProtectedGitOperation, ReferenceRepositoryMutation,
    SecurityDecisionRequired, ExternalAuthorizationRequired, ConfigurationError, MissingValidationCommand,
    ProductAssertionFailure, TestFailure, BuildFailure, EmulatorUnavailable, DeviceDisconnected,
    AdbFailure, PatrolInfrastructureFailure, Timeout,
    ProductAmbiguity, ArchitectureDecisionRequired, OpenQuestionBlocking, NonConvergingRemediation, UnknownFailure
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RecoveryAction { RetryStage, RetryAfterBackoff, RemediateValidation, RemediateReview, Wait, HumanRequired, Diagnose }

public sealed record FailureContext(
    string RunId, string TaskId, string? MilestoneId, WorkflowState Stage, WorkflowState? PreviousStage,
    FailureCategory Category, string? FailureCode, int? ExitCode, string StandardOutput, string StandardError,
    bool TimedOut, string? Provider, int? ProviderHttpStatus, DateTimeOffset? ResetAt, TimeSpan? RetryAfter,
    IReadOnlyList<PermissionDenial> PermissionDenials, IReadOnlyList<ValidationResult> ValidationFailures,
    IReadOnlyList<ReviewFinding> CodexFindings, string? RemediationOutput, bool StructuredRefusal,
    string? WorkspaceError, IReadOnlyList<PreflightCheck> PreflightFailures, GitSnapshot? Git,
    IReadOnlyDictionary<string, int> RetryCounts, IReadOnlyList<string> Warnings, string? Branch,
    IReadOnlyList<string> ArtifactPaths, string RootCause = "", bool Recoverable = false,
    RecoveryAction RecommendedAction = RecoveryAction.HumanRequired, bool HumanRequired = false,
    int EngineeringRemediationCycles = 0, bool ProgressObserved = false,
    IReadOnlyList<string>? FindingFingerprints = null);

public sealed record RecoveryDecision(RecoveryAction Action, FailureCategory Category, bool Recoverable, bool HumanRequired,
    string Reason, TimeSpan? Delay = null, DateTimeOffset? ResumeAfter = null);
public sealed record DiagnosisSuggestion(string SuggestedCategory, string LikelyRootCause, string SuggestedRecovery, double Confidence);

public sealed record DevelopmentTask(
    string Id,
    string Title,
    string Description,
    string? Type = null,
    IReadOnlyList<string>? Domains = null,
    string? Risk = null,
    IReadOnlyList<string>? Skills = null,
    IReadOnlyList<string>? RelevantContext = null,
    IReadOnlyList<string>? AcceptanceCriteria = null,
    string? ReferenceContext = null);

public sealed record RoutingResult(
    [property: JsonPropertyName("type")]
    string TaskType,
    IReadOnlyList<string> Domains,
    string Complexity,
    string Risk,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> RelevantContext,
    string RecommendedPipeline,
    string ReasoningSummary,
    bool UsedFallback = false,
    string? Warning = null);

public sealed record UsageInfo(string Provider, string? Model, long? InputTokens = null, long? OutputTokens = null, decimal? EstimatedCost = null);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StandardApplicability { Required, Consider, NotApplicable }
public sealed record EngineeringStandardExpectation(string Category, StandardApplicability Applicability, string Rationale);
public sealed record EngineeringProfile(
    string TaskKind,
    bool IsCodeTask,
    bool IsFlutterTask,
    bool IsFlutterUiTask,
    IReadOnlyList<EngineeringStandardExpectation> Standards,
    IReadOnlyList<EngineeringStandardExpectation> ExpectedValidation);
public sealed record ValidationExpectationAudit(
    string Category,
    StandardApplicability Applicability,
    IReadOnlyList<string> ConfiguredCommands,
    bool Covered);

public sealed record ProcessSpec(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory, string? StandardInput = null, TimeSpan? Timeout = null,
    Func<string, Task>? StandardOutputLineHandler = null, string? StandardOutputFile = null);
public sealed record ProcessResult(string Command, int ExitCode, string StandardOutput, string StandardError, TimeSpan Duration, bool TimedOut = false,
    string? Provider = null, int? ProviderHttpStatus = null, DateTimeOffset? ResetAt = null, TimeSpan? RetryAfter = null)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}

public sealed record PermissionDenial(string? ToolName, string RawJson, bool Blocking);
public sealed record ImplementationResult(
    bool Success,
    string Summary,
    IReadOnlyList<string> FilesChanged,
    ProcessResult? Process = null,
    UsageInfo? Usage = null,
    IReadOnlyList<PermissionDenial>? PermissionDenials = null,
    IReadOnlyList<string>? Warnings = null,
    IReadOnlyList<string>? TestsChanged = null,
    IReadOnlyDictionary<string, string>? NotApplicable = null,
    string? FailureCode = null,
    bool StructuredRefusal = false);
public enum ValidationStatus { Pass, Fail, NotExecutable, NotApplicable, ConfigurationError }

public sealed record ValidationResult(string Category, bool Required, ProcessResult Process, ValidationStatus Status = ValidationStatus.Pass, string? Reason = null)
{
    [JsonIgnore]
    public string Name => Category;
    public bool Passed => Status == ValidationStatus.Pass && Process.Succeeded;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReviewDecision { Pass, Fail }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FindingSeverity { Critical, High, Medium, Low }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum QualityStatus
{
    [JsonStringEnumMemberName("PASS")]
    Pass,
    [JsonStringEnumMemberName("FAIL")]
    Fail,
    [JsonStringEnumMemberName("NOT_APPLICABLE")]
    NotApplicable
}

public sealed record ReviewFinding(FindingSeverity Severity, string File, string Location, string Problem, string Impact, string Recommendation, bool Blocking = true);
public sealed record QualityAssessment(string Category, QualityStatus Status, string Summary, bool Blocking = false);
public sealed record PrototypeReviewEvidence(
    string ReferenceState,
    string ReferenceScreenshot,
    string ImplementationState,
    string ImplementationScreenshot);
public sealed record ReviewResult(
    ReviewDecision Decision,
    IReadOnlyList<ReviewFinding> Findings,
    string Summary,
    ProcessResult? Process = null,
    UsageInfo? Usage = null,
    IReadOnlyList<QualityAssessment>? Quality = null,
    bool IsProtocolFailure = false,
    PrototypeReviewEvidence? PrototypeEvidence = null);
public sealed record StateTransition(WorkflowState From, WorkflowState To, DateTimeOffset At, string Reason);
public sealed record GitSnapshot(string Root, string Branch, string Commit, string Status);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GitLifecycleState { NotPrepared, BranchReady, Developing, Validated, Committed, FeaturePushed, MergingToDeveloper, Merged, PostMergeValidated, DeveloperPushed, Published, HumanRequired, Failed }
public sealed record MilestoneDefinition(string Id, string Title, string Branch, string BaseBranch = "main", string? CommitMessage = null);
public sealed record GitLifecycleMetadata
{
    public string RepositoryRoot { get; set; } = "";
    public string BaseBranch { get; set; } = "developer";
    public string BaseCommit { get; set; } = "";
    public string WorkingBranch { get; set; } = "";
    public string BranchHead { get; set; } = "";
    public string? OriginBranchHead { get; set; }
    public bool WorkingTreeClean { get; set; }
    public GitLifecycleState GitLifecycleState { get; set; } = GitLifecycleState.NotPrepared;
    public string? FeaturePushSha { get; set; }
    public string? DeveloperPreMergeSha { get; set; }
    public string? MergeCommitSha { get; set; }
    public string? DeveloperPostMergeSha { get; set; }
    public string? OriginDeveloperSha { get; set; }
    public DateTimeOffset? CommitTimestamp { get; set; }
    public DateTimeOffset? PushTimestamp { get; set; }
    public DateTimeOffset? MergeTimestamp { get; set; }
    public bool? PostMergeValidation { get; set; }
    public string? GitFailure { get; set; }
    public string? GitRecoveryAction { get; set; }
}

public sealed class RunRecord
{
    public required string RunId { get; init; }
    public required DevelopmentTask Task { get; set; }
    public required DateTimeOffset StartedAt { get; init; }
    public string? MilestoneId { get; init; }
    public string? MilestoneTaskId { get; init; }
    public DateTimeOffset? EndedAt { get; set; }
    public WorkflowState State { get; set; } = WorkflowState.Created;
    public List<StateTransition> Transitions { get; } = [];
    public int RemediationCount { get; set; }
    // RemediationCount is retained as a compatibility aggregate. These counters are
    // intentionally independent so provider/protocol recovery never spends code-fix budget.
    public int EngineeringRemediationCycles { get; set; }
    public int ValidationRemediationCycles { get; set; }
    public int ProviderRecoveryAttempts { get; set; }
    public int ProtocolRecoveryAttempts { get; set; }
    public List<string> ReviewFindingFingerprints { get; } = [];
    public List<string> ReviewProgressHistory { get; } = [];
    public string? FinalDecision { get; set; }
    public RoutingResult? Routing { get; set; }
    public EngineeringProfile? EngineeringProfile { get; set; }
    public List<ValidationExpectationAudit> ValidationExpectations { get; } = [];
    public List<ValidationResult> ValidationResults { get; } = [];
    public List<ValidationResult> LatestValidationResults { get; } = [];
    public List<ReviewResult> Reviews { get; } = [];
    public GitSnapshot? Git { get; set; }
    public string? BackendReferenceArtifactPath { get; set; }
    public GitLifecycleMetadata? GitLifecycle { get; set; }
    public Dictionary<string, string?> Agents { get; } = [];
    public List<ProcessResult> Commands { get; } = [];
    public List<UsageInfo> Usage { get; } = [];
    public List<PermissionDenial> PermissionDenials { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<FailureContext> Diagnoses { get; } = [];
    public List<RecoveryRecord> Recoveries { get; } = [];
    public FailureContext? LastFailure { get; set; }
    public DateTimeOffset? ResumeAfter { get; set; }
    public Dictionary<string, int> RetryCounts { get; } = [];
    public WorkflowState? ResumeStage { get; set; }
    public WorkflowState? CurrentStage { get; set; }
    public WorkflowState? LastSuccessfulStage { get; set; }
    public RecoveryRecord? ActiveRecovery { get; set; }
    public bool ImplementationCompleted { get; set; }
    public List<string> ChangedFiles { get; } = [];
    public bool RemediationCompleted { get; set; }
    public int CurrentValidationCycle { get; set; }
    public int CurrentReviewCycle { get; set; }
    public int CurrentRemediationCycle { get; set; }
    public int RemediationContextReductionAttempts { get; set; }
    public string? RemediationContextStrategy { get; set; }
    public bool ValidationCompleted { get; set; }
    public bool ReviewCompleted { get; set; }
    public GitCheckpointDecision? GitCheckpointDecision { get; set; }
    public GitCheckpointResult? GitCheckpointResult { get; set; }
    public string? AbandonmentReason { get; set; }
    public DateTimeOffset? AbandonedAt { get; set; }

    /// <summary>
    /// Normalizes fields whose meaning is defined by the workflow state. Historical
    /// diagnoses and recoveries are deliberately not touched here; only the
    /// currently authoritative state is changed.
    /// </summary>
    public void NormalizeForState()
    {
        CurrentStage = State;
        switch (State)
        {
            case WorkflowState.Approved:
                FinalDecision = "PASS";
                LastFailure = null;
                ActiveRecovery = null;
                ResumeStage = null;
                ResumeAfter = null;
                break;
            case WorkflowState.HumanRequired:
                FinalDecision = "HUMAN_REQUIRED";
                ActiveRecovery = null;
                ResumeStage = null;
                ResumeAfter = null;
                if (LastFailure is { HumanRequired: false } failure)
                    LastFailure = failure with { Recoverable = false, RecommendedAction = RecoveryAction.HumanRequired, HumanRequired = true };
                break;
            case WorkflowState.Failed:
                FinalDecision = "FAILED";
                ActiveRecovery = null;
                ResumeStage = null;
                ResumeAfter = null;
                break;
            case WorkflowState.Completed:
                FinalDecision ??= "COMPLETED";
                ActiveRecovery = null;
                ResumeStage = null;
                ResumeAfter = null;
                break;
            case WorkflowState.Abandoned:
                FinalDecision = "ABANDONED";
                ActiveRecovery = null;
                ResumeStage = null;
                ResumeAfter = null;
                break;
            case WorkflowState.WaitingRetry:
                if (ActiveRecovery is null || ResumeStage is null)
                    throw new InvalidOperationException("WAITING_RETRY requires active recovery and resume stage metadata.");
                if (string.Equals(FinalDecision, "PASS", StringComparison.OrdinalIgnoreCase))
                    FinalDecision = "PAUSED";
                break;
            default:
                if (string.Equals(FinalDecision, "PASS", StringComparison.OrdinalIgnoreCase)) FinalDecision = null;
                break;
        }
    }
}

public sealed record RecoveryRecord(RecoveryAction Action, int Attempt, int DelaySeconds, string Result, DateTimeOffset At);

public sealed record PreflightCheck(string Name, string Status, bool Critical, string? Detail = null);
