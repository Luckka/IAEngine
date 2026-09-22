using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Pipeline;

namespace OnlineOs.AiOrchestrator.Tests;

internal static class TestData
{
    public static DevelopmentTask Task() => new("TEST-1", "Offline task", "Test offline behavior", AcceptanceCriteria: ["works"]);
    public static RunRecord Run() => new() { RunId = "RUN-TEST", Task = Task(), StartedAt = DateTimeOffset.UtcNow };
    public static RoutingResult Route() => new("feature", ["offline"], "medium", "medium", [], ["AGENTS.md"], "claude-codex", "Test route.");
    public static EngineeringProfile Engineering(DevelopmentTask? task = null, RoutingResult? route = null)
        => EngineeringStandardsPolicy.Create(task ?? Task(), route ?? Route());
    public static ReviewResult Review(ReviewDecision decision, FindingSeverity severity) => new(decision,
        [new ReviewFinding(severity, "file", "1", "problem", "impact", "fix")], "review");
    public static ValidationResult Validation(bool passed) => new("test", true, new ProcessResult("test", passed ? 0 : 1, "", "", TimeSpan.Zero));
    public static FailureContext Failure(FailureCategory category, bool humanRequired = false, WorkflowState stage = WorkflowState.Reviewing) => new(
        "RUN-TEST", "TEST-1", null, stage, WorkflowState.Validating, category, null, 1, "", "", false,
        "codex", null, null, null, [], [], [], null, false, null, [], null, new Dictionary<string, int>(), [], "feature/test", [], category.ToString(), false,
        humanRequired ? RecoveryAction.HumanRequired : RecoveryAction.RetryStage, humanRequired);
}
