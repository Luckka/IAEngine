using System.Text.RegularExpressions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Models;

namespace OnlineOs.AiOrchestrator.Pipeline;

public static class FailureClassifier
{
    public static FailureCategory Classify(WorkflowState stage, ProcessResult? process = null, string? failureCode = null,
        IReadOnlyList<PermissionDenial>? permissions = null, IReadOnlyList<ValidationResult>? validation = null,
        ReviewResult? review = null, string? message = null, bool structuredRefusal = false)
    {
        var text = string.Join(" ", message, process?.StandardOutput, process?.StandardError);
        if (IsContextOverflow(text, failureCode)) return FailureCategory.ContextOverflow;
        if (permissions?.Any(x => x.Blocking) == true || failureCode?.Contains("PERMISSION", StringComparison.OrdinalIgnoreCase) == true || text.Contains("permission_denied", StringComparison.OrdinalIgnoreCase) || text.Contains("permission denied", StringComparison.OrdinalIgnoreCase)) return FailureCategory.PermissionDenied;
        if (structuredRefusal || failureCode?.Contains("REFUS", StringComparison.OrdinalIgnoreCase) == true) return FailureCategory.ProviderStructuredRefusal;
        if (process?.ProviderHttpStatus == 429 || Regex.IsMatch(text, "rate limit|usage limit|session limit|http\\s*429|retry[- ]after|resets? at", RegexOptions.IgnoreCase)) return FailureCategory.ProviderRateLimit;
        if (process?.TimedOut == true || text.Contains("timeout", StringComparison.OrdinalIgnoreCase)) return FailureCategory.ProviderTimeout;
        if (process is not null && !process.Succeeded && IsUnavailable(text)) return Provider(stage, text);
        if (stage == WorkflowState.Reviewing && process is not null && !process.Succeeded) return FailureCategory.CodexUnavailable;
        if (validation?.Any(x => x.Required && !x.Passed) == true)
        {
            var categories = validation.Where(x => x.Required && !x.Passed).Select(x => x.Category).ToArray();
            if (categories.Any(x => x.Contains("format", StringComparison.OrdinalIgnoreCase))) return FailureCategory.FormattingFailure;
            if (categories.Any(x => x.Contains("analy", StringComparison.OrdinalIgnoreCase))) return FailureCategory.StaticAnalysisFailure;
            if (categories.Any(x => x.Contains("integration", StringComparison.OrdinalIgnoreCase))) return FailureCategory.IntegrationTestFailure;
            if (categories.Any(x => x.Contains("responsive", StringComparison.OrdinalIgnoreCase))) return FailureCategory.ResponsiveTestFailure;
            if (categories.Any(x => x.Contains("unit", StringComparison.OrdinalIgnoreCase) || x.Contains("test", StringComparison.OrdinalIgnoreCase))) return FailureCategory.UnitTestFailure;
            return FailureCategory.ValidationFailure;
        }
        if (stage == WorkflowState.Reviewing && review?.IsProtocolFailure == true) return FailureCategory.ProviderMalformedOutput;
        if (review is not null && review.Decision == ReviewDecision.Fail) return FailureCategory.ReviewFailure;
        if (stage == WorkflowState.Remediating) return FailureCategory.RemediationFailure;
        if (text.Contains("malformed", StringComparison.OrdinalIgnoreCase) || text.Contains("invalid json", StringComparison.OrdinalIgnoreCase)) return FailureCategory.ProviderMalformedOutput;
        if (text.Contains("force push", StringComparison.OrdinalIgnoreCase) || text.Contains("protected branch", StringComparison.OrdinalIgnoreCase)
            || text.Contains("destructive git", StringComparison.OrdinalIgnoreCase) || text.Contains("history rewrite", StringComparison.OrdinalIgnoreCase)) return FailureCategory.ProtectedGitOperation;
        if (text.Contains("reference repository", StringComparison.OrdinalIgnoreCase) || text.Contains("reference monolith", StringComparison.OrdinalIgnoreCase)) return FailureCategory.ReferenceRepositoryMutation;
        if (text.Contains("security decision", StringComparison.OrdinalIgnoreCase) || text.Contains("authentication boundary", StringComparison.OrdinalIgnoreCase)) return FailureCategory.SecurityDecisionRequired;
        if (text.Contains("external authorization", StringComparison.OrdinalIgnoreCase) || text.Contains("credential required", StringComparison.OrdinalIgnoreCase)) return FailureCategory.ExternalAuthorizationRequired;
        if (text.Contains("workspace", StringComparison.OrdinalIgnoreCase) && text.Contains("boundar", StringComparison.OrdinalIgnoreCase)) return FailureCategory.WorkspaceBoundaryViolation;
        if (text.Contains("ambig", StringComparison.OrdinalIgnoreCase)) return FailureCategory.ProductAmbiguity;
        return FailureCategory.UnknownFailure;
    }

    public static bool IsContextOverflow(string? text, string? failureCode = null) =>
        failureCode?.Contains("PROMPT_TOO_LONG", StringComparison.OrdinalIgnoreCase) == true
        || (!string.IsNullOrWhiteSpace(text) && (text.Contains("prompt_too_long", StringComparison.OrdinalIgnoreCase)
            || text.Contains("context_limit_exceeded", StringComparison.OrdinalIgnoreCase)
            || (text.Contains("request size", StringComparison.OrdinalIgnoreCase) && text.Contains("limit", StringComparison.OrdinalIgnoreCase))));

    private static bool IsUnavailable(string text) => text.Contains("not found", StringComparison.OrdinalIgnoreCase) || text.Contains("connection refused", StringComparison.OrdinalIgnoreCase) || text.Contains("unavailable", StringComparison.OrdinalIgnoreCase);
    private static FailureCategory Provider(WorkflowState stage, string text) =>
        text.Contains("ollama", StringComparison.OrdinalIgnoreCase) ? (text.Contains("model", StringComparison.OrdinalIgnoreCase) ? FailureCategory.OllamaModelUnavailable : FailureCategory.OllamaUnavailable) :
        stage == WorkflowState.Reviewing ? FailureCategory.CodexUnavailable : stage == WorkflowState.Routing ? FailureCategory.OllamaUnavailable : FailureCategory.ClaudeUnavailable;
}

public sealed class RecoveryPolicy(OrchestratorOptions options)
{
    public RecoveryDecision Decide(FailureContext failure)
    {
        var attempts = failure.RetryCounts.TryGetValue(failure.Category.ToString(), out var value) ? value : 0;
        var max = options.MaxRetriesFor(failure.Category);
        if (failure.Category is FailureCategory.ProductAmbiguity or FailureCategory.ArchitectureDecisionRequired or FailureCategory.OpenQuestionBlocking
            or FailureCategory.WorkspaceBoundaryViolation or FailureCategory.PermissionDenied or FailureCategory.DirtyWorkingTree
            or FailureCategory.ProtectedGitOperation or FailureCategory.ReferenceRepositoryMutation
            or FailureCategory.SecurityDecisionRequired or FailureCategory.ExternalAuthorizationRequired or FailureCategory.NonConvergingRemediation)
            return Human(failure.Category, $"Human judgment is required: {failure.Category}.");
        if (failure.Category == FailureCategory.ContextOverflow)
        {
            if (attempts >= options.RemediationContext.MaxReductionAttempts)
                return Human(failure.Category, $"Context reduction exhausted ({attempts}/{options.RemediationContext.MaxReductionAttempts}); the focused remediation request still exceeds the provider limit.");
            return new RecoveryDecision(RecoveryAction.RetryStage, failure.Category, true, false, "Rebuild remediation context at the next smaller reduction level.");
        }
        if (attempts >= max) return Human(failure.Category, $"Recovery budget exhausted ({attempts}/{max}).");
        if (failure.Category is FailureCategory.ProviderRateLimit or FailureCategory.ProviderTimeout or FailureCategory.ProviderUnavailable or FailureCategory.OllamaUnavailable or FailureCategory.OllamaModelUnavailable)
        {
            var delay = failure.RetryAfter ?? TimeSpan.FromSeconds(Math.Min(options.BackoffMaxSeconds, options.BackoffInitialSeconds * Math.Pow(2, attempts)));
            if (delay.TotalMinutes > options.AutoWaitMaxMinutes) return new RecoveryDecision(RecoveryAction.Wait, failure.Category, true, true, "Retry is beyond the automatic wait threshold.", delay, DateTimeOffset.UtcNow + delay);
            return new RecoveryDecision(RecoveryAction.RetryAfterBackoff, failure.Category, true, false, "Transient failure; retry the failed stage.", delay);
        }
        if (failure.Category == FailureCategory.ProviderMalformedOutput) return new RecoveryDecision(RecoveryAction.RetryStage, failure.Category, true, false, "Retry once with structured-output repair.");
        if (failure.Category is FailureCategory.ValidationFailure or FailureCategory.FormattingFailure or FailureCategory.StaticAnalysisFailure or FailureCategory.UnitTestFailure or FailureCategory.ResponsiveTestFailure or FailureCategory.IntegrationTestFailure)
        {
            if (failure.EngineeringRemediationCycles >= options.DeterministicValidationRemediationCycles)
                return Human(FailureCategory.NonConvergingRemediation, $"Deterministic validation remains unresolved after {failure.EngineeringRemediationCycles} remediation cycles.");
            return new RecoveryDecision(RecoveryAction.RemediateValidation, failure.Category, true, false, "Send compact deterministic failures to remediation.");
        }
        if (failure.Category == FailureCategory.ReviewFailure)
        {
            if (failure.EngineeringRemediationCycles >= options.EngineeringRemediationCycles + options.EngineeringProgressExtensions && !failure.ProgressObserved)
                return Human(FailureCategory.NonConvergingRemediation, "The same substantive review finding is not converging within the extended remediation budget.");
            return new RecoveryDecision(RecoveryAction.RemediateReview, failure.Category, true, false, "Send blocking review findings to remediation.");
        }
        if (failure.Category == FailureCategory.ProviderStructuredRefusal) return Human(failure.Category, "Provider refused the requested work.");
        return attempts < max ? new RecoveryDecision(RecoveryAction.RetryStage, failure.Category, true, false, "Bounded retry for unknown failure.") : Human(failure.Category, "Failure remains unresolved after bounded diagnosis.");
    }

    private static RecoveryDecision Human(FailureCategory category, string reason) => new(RecoveryAction.HumanRequired, category, false, true, reason);
}

public static class OrchestratorOptionsExtensions
{
    public static int MaxRetriesFor(this OrchestratorOptions options, FailureCategory category) => category switch
    {
        FailureCategory.ProviderMalformedOutput => options.MalformedOutputRetries,
        FailureCategory.UnknownFailure => options.UnknownDiagnosisRetries,
        FailureCategory.ValidationFailure or FailureCategory.FormattingFailure or FailureCategory.StaticAnalysisFailure or FailureCategory.UnitTestFailure or FailureCategory.ResponsiveTestFailure or FailureCategory.IntegrationTestFailure => options.DeterministicValidationRemediationCycles,
        FailureCategory.ReviewFailure => options.EngineeringRemediationCycles + options.EngineeringProgressExtensions,
        FailureCategory.ContextOverflow => options.RemediationContext.MaxReductionAttempts,
        _ => options.ProviderTransientRetries
    };
}
