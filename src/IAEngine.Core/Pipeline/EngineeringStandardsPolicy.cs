using OnlineOs.AiOrchestrator.Models;

namespace OnlineOs.AiOrchestrator.Pipeline;

/// <summary>
/// Technology-neutral engineering profile used by the Core workflow. Product
/// adapters may enrich the profile, but the Core does not infer a technology.
/// </summary>
public static class GenericEngineeringStandardsPolicy
{
    public static EngineeringProfile Create(DevelopmentTask task, RoutingResult route)
    {
        var code = !string.Equals(route.TaskType, "documentation", StringComparison.OrdinalIgnoreCase);
        var standards = new[]
        {
            new EngineeringStandardExpectation("acceptanceCriteria", StandardApplicability.Required, "Confirmed acceptance criteria must be satisfied."),
            new EngineeringStandardExpectation("architecture", code ? StandardApplicability.Required : StandardApplicability.NotApplicable, "Keep dependencies inward and replaceable."),
            new EngineeringStandardExpectation("solid", code ? StandardApplicability.Required : StandardApplicability.NotApplicable, "Apply SOLID pragmatically without unrelated refactoring."),
            new EngineeringStandardExpectation("unitTests", code ? StandardApplicability.Required : StandardApplicability.NotApplicable, "Cover changed behavior and failure paths."),
            new EngineeringStandardExpectation("security", StandardApplicability.Required, "Preserve repository and project security boundaries."),
            new EngineeringStandardExpectation("scope", StandardApplicability.Required, "Avoid unrelated behavior."),
        };
        var validation = new[]
        {
            new EngineeringStandardExpectation("Build", code ? StandardApplicability.Required : StandardApplicability.NotApplicable, "Run the consumer's build validation."),
            new EngineeringStandardExpectation("UnitTests", code ? StandardApplicability.Required : StandardApplicability.NotApplicable, "Run the consumer's deterministic test suite."),
            new EngineeringStandardExpectation("IntegrationTests", StandardApplicability.Consider, "Run boundary tests when the consumer changes an integration."),
            new EngineeringStandardExpectation("StaticAnalysis", code ? StandardApplicability.Required : StandardApplicability.NotApplicable, "Run configured static analysis."),
        };
        return new EngineeringProfile(
            (route.TaskType ?? task.Type ?? "unknown").ToLowerInvariant(),
            code,
            false,
            false,
            standards,
            validation);
    }
}
