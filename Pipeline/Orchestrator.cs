using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Agents;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Infrastructure;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Reference;

namespace OnlineOs.AiOrchestrator.Pipeline;

public sealed record DryRunPlan(RoutingResult Routing, EngineeringProfile Engineering, IReadOnlyList<string> ValidationCommands, string CodexReviewMode);

public sealed class Orchestrator(
    ITaskRouter router,
    IImplementationAgent implementation,
    IValidationRunner validation,
    IReviewAgent reviewer,
    IGitService git,
    IRunStore runs,
    ReviewPolicy reviewPolicy,
    WorkflowStateMachine stateMachine,
    AppOptions options,
    IProgressReporter? progressReporter = null,
    IFailureDiagnoser? diagnoser = null,
    IReferenceInspector? referenceInspector = null,
    string? expectedBranch = null)
{
    private IProgressReporter Progress { get; } = progressReporter ?? NullProgressReporter.Instance;
    private RecoveryPolicy Recovery { get; } = new(options.Orchestrator);
    private IReferenceInspector? ReferenceInspector { get; } = referenceInspector;
    private string? ExpectedBranch { get; } = expectedBranch;

    public async Task<(RunRecord Run, DryRunPlan? Plan)> ExecuteAsync(
        DevelopmentTask task,
        bool dryRun,
        CancellationToken ct = default,
        string? runId = null,
        bool headerAlreadyPrinted = false,
        string? milestoneId = null,
        string? milestoneTaskId = null)
    {
        var actualRunId = runId ?? CreateRunId();
        task = await PrepareReferenceContextAsync(task, actualRunId, ct);
        var run = new RunRecord { RunId = actualRunId, Task = task, StartedAt = DateTimeOffset.UtcNow, MilestoneId = milestoneId, MilestoneTaskId = milestoneTaskId };
        await runs.InitializeAsync(run, ct); // The CREATED record is durable before routing.
        if (IsReferenceValidationOnly(task))
        {
            run.EndedAt = DateTimeOffset.UtcNow;
            run.FinalDecision = "REFERENCE_VALIDATED";
            run.Transitions.Add(new StateTransition(WorkflowState.Created, WorkflowState.Completed, run.EndedAt.Value, "Read-only reference validation completed; no implementation pipeline was started."));
            run.State = WorkflowState.Completed;
            await runs.SaveAsync(run, ct);
            return (run, null);
        }
        var (completed, plan) = await ExecutePersistedRunAsync(run, dryRun, ct, headerAlreadyPrinted);
        return (completed, plan);
    }

    private async Task<ImplementationResult> ExecuteImplementationWithRecoveryAsync(RunRecord run, DevelopmentTask task, CancellationToken ct)
    {
        while (true)
        {
            if (!await EnsureExpectedBranchAsync(run, "before Claude implementation", ct)) return new ImplementationResult(false, "External branch change detected.", []);
            var result = await implementation.ImplementAsync(task, run.Routing!, run.EngineeringProfile!, ct);
            Record(run, result.Process, result.Usage);
            RecordPermissionAudit(run, result);
            await runs.SaveArtifactAsync(run.RunId, "implementation.json", result, ct);
            if (result.Success)
            {
                run.ImplementationCompleted = true;
                run.LastSuccessfulStage = WorkflowState.Implementing;
                await runs.SaveAsync(run, ct);
                return result;
            }
            var decision = await DiagnoseAndDecideAsync(run, WorkflowState.Implementing, result.Process, result.FailureCode, result.PermissionDenials, null, null, result.Summary, result.StructuredRefusal, ct);
            if (!await ApplyRecoveryAsync(run, decision, ct)) return result;
        }
    }

    private async Task<ImplementationResult> ExecuteRemediationWithRecoveryAsync(RunRecord run, DevelopmentTask task, IReadOnlyList<ValidationResult> validationResults, ReviewResult? reviewResult, CancellationToken ct)
    {
        while (true)
        {
            if (!await EnsureExpectedBranchAsync(run, "before Claude remediation", ct)) return new ImplementationResult(false, "External branch change detected.", []);
            var result = await implementation.RemediateAsync(task, run.EngineeringProfile!, validationResults, reviewResult, ct, run.RemediationContextReductionAttempts);
            Record(run, result.Process, result.Usage);
            RecordPermissionAudit(run, result);
            await runs.SaveArtifactAsync(run.RunId, $"remediation-{run.RemediationCount}-{run.RemediationContextReductionAttempts}.json", result, ct);
            if (result.Success)
            {
                run.RemediationCompleted = true;
                run.LastSuccessfulStage = WorkflowState.Remediating;
                await runs.SaveAsync(run, ct);
                return result;
            }
            var decision = await DiagnoseAndDecideAsync(run, WorkflowState.Remediating, result.Process, result.FailureCode, result.PermissionDenials, validationResults, reviewResult, result.Summary, result.StructuredRefusal, ct);
            if (!await ApplyRecoveryAsync(run, decision, ct)) return result;
        }
    }

    private async Task<RecoveryDecision> DiagnoseAndDecideAsync(RunRecord run, WorkflowState stage, ProcessResult? process, string? code,
        IReadOnlyList<PermissionDenial>? permissions, IReadOnlyList<ValidationResult>? validationResults, ReviewResult? review, string? message, bool refusal, CancellationToken ct,
        bool progressObserved = false, IReadOnlyList<string>? findingFingerprints = null)
    {
        var category = FailureClassifier.Classify(stage, process, code, permissions, validationResults, review, message, refusal);
        var failure = new FailureContext(run.RunId, run.Task.Id, null, stage, run.Transitions.LastOrDefault()?.From, category, code,
            process?.ExitCode, process?.StandardOutput ?? "", process?.StandardError ?? "", process?.TimedOut ?? false,
            process?.Provider, process?.ProviderHttpStatus, process?.ResetAt, process?.RetryAfter, permissions ?? [],
            validationResults?.Where(x => x.Required && !x.Passed).ToArray() ?? [], review?.Findings.Where(x => x.Blocking).ToArray() ?? [],
            stage == WorkflowState.Remediating ? message : null, refusal, null, [], run.Git, run.RetryCounts, run.Warnings,
            run.Git?.Branch, [$".ai-runs/{run.RunId}/run.json"], message ?? category.ToString(), false, RecoveryAction.HumanRequired, false,
            stage == WorkflowState.Validating ? run.ValidationRemediationCycles : run.EngineeringRemediationCycles,
            progressObserved, findingFingerprints);
        if (category == FailureCategory.UnknownFailure && diagnoser is not null && (run.RetryCounts.TryGetValue("UnknownDiagnosis", out var diagnosisAttempts) ? diagnosisAttempts : 0) < options.Orchestrator.UnknownDiagnosisRetries)
        {
            run.RetryCounts["UnknownDiagnosis"] = diagnosisAttempts + 1;
            var suggestion = await diagnoser.DiagnoseAsync(failure, ct);
            if (suggestion is not null && Enum.TryParse<FailureCategory>(suggestion.SuggestedCategory, true, out var suggested))
            {
                category = suggested;
                failure = failure with { Category = category, RootCause = suggestion.LikelyRootCause };
            }
        }
        var decision = Recovery.Decide(failure);
        failure = failure with { RootCause = decision.Reason, Recoverable = decision.Recoverable, RecommendedAction = decision.Action, HumanRequired = decision.HumanRequired };
        run.LastFailure = failure;
        run.Diagnoses.Add(failure);
        await runs.SaveArtifactAsync(run.RunId, $"diagnosis-{run.Diagnoses.Count}.json", failure, ct);
        await runs.SaveAsync(run, ct);
        return decision;
    }

    private async Task<bool> ApplyRecoveryAsync(RunRecord run, RecoveryDecision decision, CancellationToken ct)
    {
        var key = decision.Category.ToString();
        run.RetryCounts[key] = run.RetryCounts.TryGetValue(key, out var count) ? count + 1 : 1;
        if (decision.Category is FailureCategory.ProviderRateLimit or FailureCategory.ProviderUnavailable or FailureCategory.ProviderTimeout
            or FailureCategory.OllamaUnavailable or FailureCategory.OllamaModelUnavailable or FailureCategory.CodexUnavailable or FailureCategory.ClaudeUnavailable)
            run.ProviderRecoveryAttempts++;
        if (decision.Category == FailureCategory.ProviderMalformedOutput) run.ProtocolRecoveryAttempts++;
        if (decision.Category == FailureCategory.ContextOverflow)
        {
            run.RemediationContextReductionAttempts = run.RetryCounts[key];
            run.RemediationContextStrategy = run.RemediationContextReductionAttempts >= options.Orchestrator.RemediationContext.MaxReductionAttempts ? "exhausted" : "rebuilding-focused";
        }
        var delay = decision.Delay ?? TimeSpan.Zero;
        var recovery = new RecoveryRecord(decision.Action, run.RetryCounts[key], (int)delay.TotalSeconds, decision.Reason, DateTimeOffset.UtcNow);
        run.Recoveries.Add(recovery);
        run.ActiveRecovery = recovery;
        await runs.SaveArtifactAsync(run.RunId, $"recovery-{run.Recoveries.Count}.json", recovery, ct);
        if (decision.Action == RecoveryAction.RetryAfterBackoff)
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
            return true;
        }
        if (decision.Action == RecoveryAction.RetryStage)
        {
            var message = decision.Category == FailureCategory.ContextOverflow
                ? $"WAITING_RETRY: ContextOverflow | reduction {run.RemediationContextReductionAttempts}/{options.Orchestrator.RemediationContext.MaxReductionAttempts} | rebuilding {run.RemediationContextStrategy} remediation context"
                : $"WAITING_RETRY: {decision.Category} | retrying failed stage";
            Progress.CompleteStage(message, false);
            return true;
        }
        run.ResumeAfter = decision.ResumeAfter;
        run.ResumeStage = run.LastFailure?.Stage;
        // Preserve the historical terminal FAILED state for an implementation process
        // that never produced a task result; the persisted diagnosis still makes the
        // required human action explicit and avoids the old opaque artifact-only message.
        var terminal = decision.HumanRequired && run.LastFailure?.Stage == WorkflowState.Implementing && decision.Category == FailureCategory.PermissionDenied
            ? WorkflowState.Failed
            : decision.HumanRequired ? WorkflowState.HumanRequired : WorkflowState.WaitingRetry;
        var transitionReason = decision.Category == FailureCategory.PermissionDenied && run.LastFailure?.Stage == WorkflowState.Implementing
            ? ClaudeAgent.PermissionDeniedReason : decision.Reason;
        await MoveAsync(run, terminal, transitionReason, ct);
        await CompleteAsync(run, decision.HumanRequired ? "HUMAN_REQUIRED" : "PAUSED", ct);
        Progress.CompleteStage(decision.HumanRequired ? $"HUMAN REQUIRED: {decision.Reason}" : $"WAITING_RETRY: {decision.Category} | {decision.Reason}", false);
        return false;
    }

    private static void Record(RunRecord run, ProcessResult? process, UsageInfo? usage)
    {
        if (process is not null) run.Commands.Add(process);
        if (usage is not null) run.Usage.Add(usage);
    }

    public async Task<RunRecord> ContinueAsync(RunRecord run, CancellationToken ct = default)
    {
        if (run.State == WorkflowState.HumanRequired)
        {
            if (run.Reviews.Count == 0)
            {
                var persistedReview = await runs.LoadLatestArtifactAsync<ReviewResult>(run.RunId, "review-", ct);
                if (persistedReview is not null) run.Reviews.Add(persistedReview);
            }
            await ReconcilePersistedContextOverflowAsync(run, ct);
            if (run.LastFailure is { Stage: var providerStage, Category: var providerCategory }
                && IsTransientProviderFailure(providerStage, providerCategory)
                && run.Routing is not null && run.EngineeringProfile is not null && run.ImplementationCompleted)
                return await RetryHumanRequiredAsync(run, ct);
            if (run.LastFailure is { Stage: WorkflowState.Remediating, Category: FailureCategory.ContextOverflow }
                && run.Routing is not null && run.EngineeringProfile is not null && run.ImplementationCompleted)
                return await RetryHumanRequiredAsync(run, ct);
            try
            {
                stateMachine.ReopenForRoutineEngineeringRecovery(run,
                    "Autonomous recovery: legacy HumanRequired state was caused by routine engineering remediation exhaustion; preserving history and continuing under the convergence-aware policy.");
            }
            catch (InvalidOperationException)
            {
                return run;
            }
            run.EndedAt = null;
            run.FinalDecision = null;
            run.ActiveRecovery = null;
            run.ResumeAfter = null;
            run.ResumeStage = null;
            run.RemediationCompleted = false;
            run.ValidationCompleted = false;
            run.ReviewCompleted = false;
            run.LatestValidationResults.Clear();
            await runs.SaveAsync(run, ct);
        }
        if (ReferenceInspector is not null && IsReferenceApplicable(run.Task) && string.IsNullOrWhiteSpace(run.Task.ReferenceContext))
        {
            run.Task = await PrepareReferenceContextAsync(run.Task, run.RunId, ct);
            await runs.SaveArtifactAsync(run.RunId, "task.json", run.Task, ct);
            await runs.SaveAsync(run, ct);
        }
        return (await ExecutePersistedRunAsync(run, false, ct, true)).Run;
    }

    public async Task<RunRecord> RetryHumanRequiredAsync(RunRecord run, CancellationToken ct = default)
    {
        if (run.State != WorkflowState.HumanRequired)
            throw new InvalidOperationException($"Run {run.RunId} is not HumanRequired; explicit retry was not applied.");
        var failure = run.LastFailure ?? FindInterruptedAuthorizedTransientFailure(run);
        failure ??= await runs.LoadArtifactAsync<FailureContext>(run.RunId, "diagnosis-1.json", ct);
        failure ??= await runs.LoadLatestArtifactAsync<FailureContext>(run.RunId, "diagnosis-", ct);
        if (failure is not null && !IsTransientProviderFailure(failure.Stage, failure.Category)
            && !(failure.Stage == WorkflowState.Remediating && failure.Category == FailureCategory.ContextOverflow)) failure = null;
        if (run.LastFailure is null && failure is not null) run.LastFailure = failure;
        var reviewFailure = failure is { Category: FailureCategory.ReviewFailure, Stage: WorkflowState.Reviewing };
        var transientProvider = failure is not null && IsTransientProviderFailure(failure.Stage, failure.Category);
        var contextOverflow = failure is { Category: FailureCategory.ContextOverflow, Stage: WorkflowState.Remediating };
        if (!reviewFailure && !transientProvider && !contextOverflow)
            throw new InvalidOperationException($"Run {run.RunId} is not eligible for authorized recovery.");
        if (!run.ImplementationCompleted || run.Routing is null || run.EngineeringProfile is null)
            throw new InvalidOperationException($"Run {run.RunId} has no preserved implementation context; recovery was not applied.");

        var reason = contextOverflow
            ? "Autonomous recovery: remediation context overflow was reconciled from structured provider evidence; rebuilding focused context."
            : reviewFailure
            ? "Human-authorized recovery: reviewer infrastructure recovery; deterministic validation will be rerun before Codex review."
            : "Human-authorized recovery: transient provider recovery; deterministic validation will be rerun before Codex review.";
        if (contextOverflow) stateMachine.ReopenForContextOverflowRecovery(run, reason);
        else if (reviewFailure) stateMachine.ReopenForAuthorizedReviewRecovery(run, reason);
        else stateMachine.ReopenForAuthorizedTransientProviderRecovery(run, reason);

        // Preserve historical audit artifacts; reset only active execution bookkeeping.
        run.EndedAt = null;
        run.FinalDecision = null;
        run.ActiveRecovery = null;
        run.ResumeAfter = null;
        run.ResumeStage = null;
        if (reviewFailure) { run.RemediationCount = 0; run.CurrentRemediationCycle = 0; }
        run.RemediationCompleted = false;
        run.ValidationCompleted = false;
        run.LatestValidationResults.Clear();
        run.ReviewCompleted = false;
        run.RetryCounts.Remove(failure!.Category.ToString());
        if (contextOverflow) { run.RemediationContextReductionAttempts = 0; run.RemediationContextStrategy = null; }
        await runs.SaveAsync(run, ct);
        return (await ExecutePersistedRunAsync(run, false, ct, true)).Run;
    }

    private static bool IsTransientProviderFailure(WorkflowState stage, FailureCategory category) => stage switch
    {
        WorkflowState.Reviewing => category is FailureCategory.ProviderRateLimit or FailureCategory.ProviderUnavailable or FailureCategory.ProviderTimeout or FailureCategory.ProviderMalformedOutput or FailureCategory.CodexUnavailable,
        WorkflowState.Remediating => category is FailureCategory.ProviderRateLimit or FailureCategory.ProviderUnavailable or FailureCategory.ProviderTimeout or FailureCategory.ProviderMalformedOutput or FailureCategory.ClaudeUnavailable,
        _ => false
    };

    private async Task ReconcilePersistedContextOverflowAsync(RunRecord run, CancellationToken ct)
    {
        var candidates = new[] { run.LastFailure }.Concat(run.Diagnoses.Cast<FailureContext>()).Where(x => x is not null).Cast<FailureContext>();
        var evidence = candidates.LastOrDefault(x => FailureClassifier.IsContextOverflow(
            string.Join(" ", x.StandardOutput, x.StandardError, x.RootCause), x.FailureCode));
        if (evidence is null || evidence.Stage != WorkflowState.Remediating || evidence.Category == FailureCategory.ContextOverflow) return;
        var reconciled = evidence with
        {
            Category = FailureCategory.ContextOverflow,
            RootCause = "Reconciled persisted diagnosis: Claude reported prompt_too_long; remediation context will be reduced.",
            Recoverable = true,
            RecommendedAction = RecoveryAction.RetryStage,
            HumanRequired = false
        };
        run.LastFailure = reconciled;
        run.Diagnoses.Add(reconciled);
        run.RetryCounts.Remove(FailureCategory.ReviewFailure.ToString());
        await runs.SaveArtifactAsync(run.RunId, $"diagnosis-{run.Diagnoses.Count}.json", reconciled, ct);
        await runs.SaveAsync(run, ct);
    }

    private static FailureContext? FindInterruptedAuthorizedTransientFailure(RunRecord run)
    {
        var recoveryWasAuthorized = run.Transitions.Any(x =>
            x.From == WorkflowState.HumanRequired && x.To == WorkflowState.Validating
            && x.Reason.StartsWith("Human-authorized recovery: transient provider", StringComparison.Ordinal));
        var stoppedDuringRecovery = run.Transitions.LastOrDefault() is
            { From: WorkflowState.Validating, To: WorkflowState.HumanRequired, Reason: "Maximum remediation cycles reached." };
        return recoveryWasAuthorized && stoppedDuringRecovery
            ? run.Diagnoses.LastOrDefault(x => IsTransientProviderFailure(x.Stage, x.Category))
            : null;
    }

    private async Task<DevelopmentTask> PrepareReferenceContextAsync(DevelopmentTask task, string? runId, CancellationToken ct)
    {
        if (ReferenceInspector is null || !IsReferenceApplicable(task)) return task;
        var artifact = await ReferenceInspector.InspectAsync(task, ct);
        var context = BuildReferenceSummary(artifact);
        if (runId is not null)
            await runs.SaveArtifactAsync(runId, options.Reference.ArtifactName, artifact, ct);
        return task with { ReferenceContext = context };
    }

    public static bool IsReferenceApplicable(DevelopmentTask task) =>
        task.Id.StartsWith("M2", StringComparison.OrdinalIgnoreCase)
        || task.Id.StartsWith("M3", StringComparison.OrdinalIgnoreCase)
        || task.Id.StartsWith("M4", StringComparison.OrdinalIgnoreCase)
        || task.Id.StartsWith("M5", StringComparison.OrdinalIgnoreCase)
        || (task.Domains?.Contains("flutter", StringComparer.OrdinalIgnoreCase) == true && task.Domains.Contains("milestone", StringComparer.OrdinalIgnoreCase));

    public static bool IsReferenceValidationOnly(DevelopmentTask task)
    {
        var text = $"{task.Id} {task.Title} {task.Description}";
        return text.Contains("read-only", StringComparison.OrdinalIgnoreCase)
            && (text.Contains("reference", StringComparison.OrdinalIgnoreCase) || text.Contains("validation", StringComparison.OrdinalIgnoreCase));
    }

    public static string BuildReferenceSummary(BackendReferenceArtifact artifact)
    {
        var confirmed = artifact.ConfirmedContracts.Take(12).Select(x => $"- {x.Concept}: {x.BackendType ?? "public contract source"} ({x.Classification})");
        var deferred = artifact.DeferredItems.Concat(artifact.BackendGaps).Take(8).Select(x => $"- {x.Description} Keep behind repository abstraction; {x.FutureAction}");
        return $"REFERENCE SYSTEM: OnlineOS monolith\nBranch: {artifact.ReferenceBranch}\nCommit: {artifact.ReferenceCommit}\nStatus: {artifact.ReferenceStatus}\nRelevant confirmed/selected contracts:\n{string.Join("\n", confirmed.DefaultIfEmpty("- None confirmed; use product docs and local mocks."))}\nDeferred compatibility:\n{string.Join("\n", deferred.DefaultIfEmpty("- None."))}\nIMPLEMENTATION RULES: Flutter remains runtime-independent from backend; use mocks/local data; no real HTTP integration; do not modify the reference repository; legacy frontend is secondary evidence only.";
    }

    /// The only workflow executor. New and resumed runs enter here with different
    /// initialization, then use the persisted state and completion markers identically.
    private async Task<(RunRecord Run, DryRunPlan? Plan)> ExecutePersistedRunAsync(
        RunRecord run, bool dryRun, CancellationToken ct, bool headerAlreadyPrinted)
    {
        if (run.State is WorkflowState.Approved or WorkflowState.HumanRequired or WorkflowState.Failed or WorkflowState.Completed or WorkflowState.Abandoned)
            return (run, null);
        try
        {
            await InitializeRunContextAsync(run, headerAlreadyPrinted, ct);
            while (run.State is not (WorkflowState.Approved or WorkflowState.HumanRequired or WorkflowState.Failed))
            {
                if (run.State == WorkflowState.WaitingRetry)
                {
                    if (run.ResumeAfter is not null && run.ResumeAfter > DateTimeOffset.UtcNow) return (run, null);
                    await MoveAsync(run, run.ResumeStage ?? WorkflowState.Implementing, "Persisted retry window reached; resuming the failed stage.", ct);
                }

                switch (run.State)
                {
                    case WorkflowState.Created:
                        await MoveAsync(run, WorkflowState.Routing, "Task routing started.", ct);
                        break;
                    case WorkflowState.Routing:
                        await ExecuteRoutingStageAsync(run, ct);
                        break;
                    case WorkflowState.Routed:
                        if (dryRun) return (run, await CreateDryRunPlanAsync(run, ct));
                        var safety = await git.CheckBranchSafetyAsync(ct);
                        if (!safety.Safe) { await FailAsync(run, safety.Reason, ct); break; }
                        Progress.StartStage("Claude implementing", "Claude still working");
                        await MoveAsync(run, WorkflowState.Implementing, "Claude implementation started.", ct);
                        break;
                    case WorkflowState.Implementing:
                        if (!run.ImplementationCompleted)
                        {
                            var baselineStatus = await git.GetStatusAsync(ct);
                            var baselineDiff = await git.GetDiffAsync(ct);
                            var result = await ExecuteImplementationWithRecoveryAsync(run, run.Task, ct);
                            if (!result.Success) break;
                            if (result.PermissionDenials?.Any(x => !x.Blocking) == true
                                && !await HasProducedChangesAsync(baselineStatus, baselineDiff, ct))
                            {
                                await FailAsync(run, ClaudeAgent.PermissionDeniedReason, ct);
                                break;
                            }
                        }
                        await MoveAsync(run, WorkflowState.Validating, "Implementation is complete; continuing validation.", ct);
                        break;
                    case WorkflowState.Validating:
                        if (!await EnsureExpectedBranchAsync(run, "before deterministic validation", ct)) break;
                        await ExecuteValidationStageAsync(run, ct);
                        break;
                    case WorkflowState.Reviewing:
                        if (!await EnsureExpectedBranchAsync(run, "before Codex review", ct)) break;
                        await ExecuteReviewStageAsync(run, ct);
                        break;
                    case WorkflowState.Remediating:
                        await ExecuteRemediationStageAsync(run, ct);
                        break;
                }
            }
            return (run, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            if (run.State is not (WorkflowState.Approved or WorkflowState.HumanRequired or WorkflowState.Failed))
                await FailAsync(run, exception.Message, CancellationToken.None);
            return (run, null);
        }
    }

    private async Task InitializeRunContextAsync(RunRecord run, bool headerAlreadyPrinted, CancellationToken ct)
    {
        if (run.Git is null)
        {
            run.Git = new GitSnapshot(await git.GetRootAsync(ct), await git.GetBranchAsync(ct), await git.GetCommitAsync(ct), await git.GetStatusAsync(ct));
            if (!headerAlreadyPrinted) Progress.PrintHeader(run.RunId, run.Task.Title, run.Git.Branch);
            run.Agents["ollama"] = options.Ollama.Model;
            run.Agents["claude"] = options.Claude.Command;
            run.Agents["codex"] = options.Codex.Command;
            await runs.SaveAsync(run, ct);
        }
        // Older run records may have routing without the derived profile fields.
        // Reconstruct only missing derived data; historical artifacts remain untouched.
        if (run.Routing is not null && run.EngineeringProfile is null)
        {
            run.EngineeringProfile = EngineeringStandardsPolicy.Create(run.Task, run.Routing);
            if (run.ValidationExpectations.Count == 0) RecordValidationExpectations(run, run.EngineeringProfile);
            await runs.SaveArtifactAsync(run.RunId, "engineering-profile.json", run.EngineeringProfile, ct);
            await runs.SaveArtifactAsync(run.RunId, "validation-expectations.json", run.ValidationExpectations, ct);
            await runs.SaveAsync(run, ct);
        }
        // Older run artifacts only had the aggregate remediation counter. Seed the
        // new independent engineering budget without rewriting historical evidence.
        if (run.EngineeringRemediationCycles == 0 && run.RemediationCount > 0 && run.Reviews.Any(x => x.Decision == ReviewDecision.Fail))
        {
            run.EngineeringRemediationCycles = run.RemediationCount;
            await runs.SaveAsync(run, ct);
        }
    }

    private async Task ExecuteRoutingStageAsync(RunRecord run, CancellationToken ct)
    {
        Progress.StartStage("Routing with Ollama", "Ollama still routing");
        if (run.Routing is null)
        {
            run.Routing = await router.RouteAsync(run.Task, ct);
            run.EngineeringProfile = EngineeringStandardsPolicy.Create(run.Task, run.Routing);
            RecordValidationExpectations(run, run.EngineeringProfile);
            await runs.SaveArtifactAsync(run.RunId, "routing.json", run.Routing, ct);
            await runs.SaveArtifactAsync(run.RunId, "engineering-profile.json", run.EngineeringProfile, ct);
            await runs.SaveArtifactAsync(run.RunId, "validation-expectations.json", run.ValidationExpectations, ct);
        }
        await MoveAsync(run, WorkflowState.Routed, run.Routing.UsedFallback ? "Task routed with deterministic fallback." : "Task routed by Ollama.", ct);
        Progress.CompleteStage($"Routed: {run.Routing.TaskType} | risk={run.Routing.Risk} | pipeline={run.Routing.RecommendedPipeline}");
    }

    private async Task<DryRunPlan> CreateDryRunPlanAsync(RunRecord run, CancellationToken ct)
    {
        var plan = new DryRunPlan(run.Routing!, run.EngineeringProfile!,
            options.Validation.Commands.Where(x => x.Enabled)
                .Select(x => string.Join(' ', new[] { x.Command }.Concat(x.Arguments))).ToArray(),
            "Codex read-only engineering-quality review of task, repository context, tests, and git diff.");
        run.EndedAt = DateTimeOffset.UtcNow;
        run.FinalDecision = "DRY_RUN";
        stateMachine.Move(run, WorkflowState.Completed, "Dry-run routing completed; no implementation pipeline was started.");
        await runs.SaveArtifactAsync(run.RunId, "dry-run.json", plan, ct);
        await runs.SaveAsync(run, ct);
        return plan;
    }

    private async Task ExecuteValidationStageAsync(RunRecord run, CancellationToken ct)
    {
        if (!await EnsureExpectedBranchAsync(run, "before deterministic validation", ct)) return;
        Progress.StartStage(run.CurrentValidationCycle == 0 ? "Running deterministic validation" : "Running deterministic re-validation", "Validation still running");
        if (!run.ValidationCompleted)
        {
            run.CurrentValidationCycle++;
            var results = await validation.RunAsync(run.EngineeringProfile ?? EngineeringStandardsPolicy.Create(run.Task, run.Routing!), ct);
            run.ValidationResults.AddRange(results);
            run.LatestValidationResults.Clear();
            run.LatestValidationResults.AddRange(results);
            run.Commands.AddRange(results.Select(x => x.Process));
            run.ValidationCompleted = true;
            await runs.SaveArtifactAsync(run.RunId, $"validation-{run.CurrentValidationCycle}.json", results, ct);
            await runs.SaveAsync(run, ct);
        }

        var failed = run.LatestValidationResults.Any(x => x.Required && !x.Passed);
        Progress.CompleteStage(failed ? "Validation failed" : "Validation passed", !failed);
        if (failed)
        {
            await DiagnoseAndDecideAsync(run, WorkflowState.Validating, null, null, null, run.LatestValidationResults, null, "Deterministic validation failed.", false, ct);
            if (run.ValidationRemediationCycles >= options.Orchestrator.DeterministicValidationRemediationCycles)
            {
                run.LastFailure = run.LastFailure! with
                {
                    Category = FailureCategory.NonConvergingRemediation,
                    RootCause = $"Deterministic validation remains unresolved after {run.ValidationRemediationCycles} remediation cycles.",
                    Recoverable = false,
                    RecommendedAction = RecoveryAction.HumanRequired,
                    HumanRequired = true
                };
                await CompleteAsHumanAsync(run, run.LastFailure.RootCause, ct);
                return;
            }
            await MoveAsync(run, WorkflowState.Remediating, "Validation requires remediation.", ct);
            run.ValidationRemediationCycles++;
            run.RemediationCount++;
            run.CurrentRemediationCycle = run.RemediationCount;
            run.RemediationCompleted = false;
            await runs.SaveAsync(run, ct);
            return;
        }

        run.ReviewCompleted = false;
        await MoveAsync(run, WorkflowState.Reviewing, "Validation passed; continuing review.", ct);
    }

    private async Task ExecuteReviewStageAsync(RunRecord run, CancellationToken ct)
    {
        if (!await EnsureExpectedBranchAsync(run, "before Codex review", ct)) return;
        Progress.StartStage(run.CurrentReviewCycle == 0 ? "Codex reviewing" : "Codex re-reviewing", "Codex still reviewing");
        if (!run.ReviewCompleted)
        {
            run.CurrentReviewCycle++;
            var review = await reviewer.ReviewAsync(run.Task, run.EngineeringProfile!, run.LatestValidationResults, await git.GetDiffAsync(ct), ct);
            run.Reviews.Add(review);
            Record(run, review.Process, review.Usage);
            await runs.SaveArtifactAsync(run.RunId, $"review-{run.CurrentReviewCycle}.json", review, ct);
            run.ReviewCompleted = true;
            await runs.SaveAsync(run, ct);
            if (review.Process is not null && !review.Process.Succeeded)
            {
                var decision = await DiagnoseAndDecideAsync(run, WorkflowState.Reviewing, review.Process, null, null, null, review, review.Summary, false, ct);
                if (await ApplyRecoveryAsync(run, decision, ct)) run.ReviewCompleted = false;
                return;
            }
            if (review.IsProtocolFailure)
            {
                var decision = await DiagnoseAndDecideAsync(run, WorkflowState.Reviewing, review.Process, null, null, null, review, review.Summary, false, ct);
                if (await ApplyRecoveryAsync(run, decision, ct)) run.ReviewCompleted = false;
                return;
            }
        }

        var latest = run.Reviews[^1];
        if (reviewPolicy.Passes(latest))
        {
            Progress.CompleteStage("Review passed");
            Progress.StartStage("Approved");
            await MoveAsync(run, WorkflowState.Approved, "Review policy passed.", ct);
            await CompleteAsync(run, "PASS", ct);
            Progress.CompleteStage("Approved");
            return;
        }
        Progress.CompleteStage("Review requires remediation", false);
        var progress = ReviewConvergence.Observe(run, latest);
        var canContinue = run.EngineeringRemediationCycles < options.Orchestrator.EngineeringRemediationCycles
            || (progress.ProgressObserved && run.EngineeringRemediationCycles < options.Orchestrator.EngineeringRemediationCycles + options.Orchestrator.EngineeringProgressExtensions);
        await DiagnoseAndDecideAsync(run, WorkflowState.Reviewing, latest.Process, null, null, null, latest, latest.Summary, false, ct,
            progress.ProgressObserved, progress.FindingFingerprints);
        if (!canContinue)
        {
            run.LastFailure = run.LastFailure! with
            {
                Category = FailureCategory.NonConvergingRemediation,
                RootCause = $"The same substantive review finding remains after {run.EngineeringRemediationCycles} remediation cycles with no meaningful progress.",
                Recoverable = false,
                RecommendedAction = RecoveryAction.HumanRequired,
                HumanRequired = true
            };
            await CompleteAsHumanAsync(run, run.LastFailure.RootCause, ct);
            return;
        }
        await MoveAsync(run, WorkflowState.Remediating, "Review requires remediation.", ct);
        run.EngineeringRemediationCycles++;
        run.RemediationCount++;
        run.CurrentRemediationCycle = run.RemediationCount;
        run.RemediationCompleted = false;
        await runs.SaveAsync(run, ct);
    }

    private async Task ExecuteRemediationStageAsync(RunRecord run, CancellationToken ct)
    {
        Progress.StartStage("Claude remediation", "Claude still remediating");
        var review = run.Reviews.LastOrDefault();
        var result = await ExecuteRemediationWithRecoveryAsync(run, run.Task, run.LatestValidationResults, review, ct);
        if (!result.Success) return;
        Progress.CompleteStage("Claude remediation completed");
        await MoveAsync(run, WorkflowState.Validating, $"Remediation cycle {run.RemediationCount} completed.", ct);
        run.ValidationCompleted = false;
        run.ReviewCompleted = false;
        await runs.SaveAsync(run, ct);
    }

    private async Task CompleteAsHumanAsync(RunRecord run, string reason, CancellationToken ct)
    {
        await MoveAsync(run, WorkflowState.HumanRequired, reason, ct);
        await CompleteAsync(run, "HUMAN_REQUIRED", ct);
    }

    private async Task<bool> EnsureExpectedBranchAsync(RunRecord run, string stage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ExpectedBranch) || await git.IsOnBranchAsync(ExpectedBranch, ct)) return true;
        await CompleteAsHumanAsync(run, $"External branch change detected {stage}; expected '{ExpectedBranch}'.", ct);
        return false;
    }

    private static void RecordPermissionAudit(RunRecord run, ImplementationResult result)
    {
        if (result.PermissionDenials is not null) run.PermissionDenials.AddRange(result.PermissionDenials);
        if (result.Warnings is not null) run.Warnings.AddRange(result.Warnings);
    }

    private void RecordValidationExpectations(RunRecord run, EngineeringProfile engineering)
    {
        var configured = options.Validation.Commands.Where(x => x.Enabled).Select(x => $"{(string.IsNullOrWhiteSpace(x.Category) ? x.Name : x.Category)} {x.Command} {string.Join(' ', x.Arguments)}").ToArray();
        foreach (var expectation in engineering.ExpectedValidation)
        {
            var terms = expectation.Category switch
            {
                "Build" => new[] { "build" },
                "UnitTests" => new[] { "test" },
                "IntegrationTests" => new[] { "integration" },
                "Format" => new[] { "format" },
                "StaticAnalysis" => new[] { "analyze", "analysis" },
                "ResponsiveWidgetTests" => new[] { "test" },
                _ => []
            };
            var matches = configured.Where(command => terms.Any(term => command.Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();
            var covered = expectation.Applicability == StandardApplicability.NotApplicable || matches.Length > 0;
            run.ValidationExpectations.Add(new ValidationExpectationAudit(expectation.Category, expectation.Applicability, matches, covered));
            if (expectation.Applicability == StandardApplicability.Required && !covered)
                run.Warnings.Add($"Required deterministic validation category is not configured: {expectation.Category}.");
        }
    }

    private async Task<bool> HasProducedChangesAsync(string baselineStatus, string baselineDiff, CancellationToken ct)
    {
        var currentStatus = await git.GetStatusAsync(ct);
        var currentDiff = await git.GetDiffAsync(ct);
        return !string.Equals(baselineStatus, currentStatus, StringComparison.Ordinal)
            || !string.Equals(baselineDiff, currentDiff, StringComparison.Ordinal);
    }

    private async Task MoveAsync(RunRecord run, WorkflowState next, string reason, CancellationToken ct)
    {
        stateMachine.Move(run, next, reason);
        run.CurrentStage = next;
        if (next == WorkflowState.Routed) run.LastSuccessfulStage = WorkflowState.Routing;
        if (next == WorkflowState.Reviewing) run.LastSuccessfulStage = WorkflowState.Validating;
        await runs.SaveAsync(run, ct);
    }

    private async Task FailAsync(RunRecord run, string reason, CancellationToken ct)
    {
        var safeReason = SafeProgressFailureReason(run.State, reason);
        Progress.CompleteStage(safeReason, false);
        Progress.StartStage("Failed");
        await MoveAsync(run, WorkflowState.Failed, reason, ct);
        await CompleteAsync(run, "FAILED", ct);
        Progress.CompleteStage($"Failed: {safeReason}", false);
    }

    private async Task CompleteAsync(RunRecord run, string decision, CancellationToken ct)
    {
        run.EndedAt = DateTimeOffset.UtcNow;
        run.FinalDecision = decision;
        await runs.SaveAsync(run, ct);
    }

    public static string CreateRunId() => $"RUN-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..28];

    private static string SafeProgressFailureReason(WorkflowState stage, string reason)
    {
        if (string.Equals(reason, ClaudeAgent.PermissionDeniedReason, StringComparison.Ordinal)) return reason;
        if (reason.StartsWith("Branch '", StringComparison.Ordinal) || reason.StartsWith("Detached HEAD", StringComparison.Ordinal)) return reason;
        return $"{stage} failed. See the run artifacts for details.";
    }
}
