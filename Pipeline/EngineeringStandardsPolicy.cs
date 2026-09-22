using OnlineOs.AiOrchestrator.Models;

namespace OnlineOs.AiOrchestrator.Pipeline;

public static class EngineeringStandardsPolicy
{
    public static readonly string[] GeneralReviewCategories =
    [
        "acceptanceCriteria", "architecture", "solid", "cleanCode", "unitTests",
        "integrationTests", "regressionTests", "security", "scope"
    ];

    public static readonly string[] FlutterUiReviewCategories =
    [
        "responsiveDesign", "adaptiveLayout", "overflowSafety", "textScaling",
        "localizationLayoutSafety", "keyboardSafety", "designSystemCompliance",
        "responsiveTests", "accessibility", "prototypeConformity"
    ];

    public static EngineeringProfile Create(DevelopmentTask task, RoutingResult route)
    {
        var text = string.Join(' ', new[] { task.Type, task.Title, task.Description, task.Risk, route.TaskType, route.RecommendedPipeline }
            .Concat(task.Domains ?? [])
            .Concat(task.Skills ?? [])
            .Concat(task.RelevantContext ?? [])
            .Concat(task.AcceptanceCriteria ?? [])
            .Concat(route.Domains)
            .Concat(route.Skills)
            .Concat(route.RelevantContext)).ToLowerInvariant();
        var words = text.Split([' ', '-', '_', '/', '.', ',', ':', ';', '(', ')'], StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var taskKind = (route.TaskType ?? task.Type ?? "unknown").ToLowerInvariant();
        var executableKinds = new[] { "feature", "bug", "refactor", "architecture", "test", "security" };
        var explicitCodeSignal = executableKinds.Contains(taskKind, StringComparer.OrdinalIgnoreCase)
            || executableKinds.Contains((task.Type ?? "").ToLowerInvariant(), StringComparer.OrdinalIgnoreCase)
            || new[] { "implement", "implementation", "bootstrap", "foundation", "code", "widget", "screen", "api", "database", "persistence" }.Any(words.Contains)
            || text.Contains("dependency injection");
        var documentation = !explicitCodeSignal
            && (taskKind == "documentation" || HasDomain(route, "documentation") || words.Contains("documentation"));
        var bug = taskKind == "bug" || text.Contains("bug fix") || text.Contains("regression");
        var flutterSignal = words.Contains("flutter") || words.Contains("dart") || text.Contains("flutter/")
            || task.Skills?.Any(x => x.Contains("flutter", StringComparison.OrdinalIgnoreCase)) == true
            || task.RelevantContext?.Any(x => x.Contains("flutter", StringComparison.OrdinalIgnoreCase)) == true
            || route.Skills.Any(x => x.Contains("flutter", StringComparison.OrdinalIgnoreCase))
            || route.RelevantContext.Any(x => x.Contains("flutter", StringComparison.OrdinalIgnoreCase));
        var flutter = !documentation && flutterSignal;
        var flutterUi = flutter && (HasDomain(route, "ui") || HasDomain(route, "design")
            || new[] { "screen", "widget", "layout", "responsive", "adaptive", "form", "design", "navigation", "accessibility", "localization", "viewport" }.Any(words.Contains)
            || text.Contains("design system") || text.Contains("dependency injection") && text.Contains("presentation"));
        var boundary = !documentation && (new[] { "api", "database", "http", "persistence", "repository", "storage", "authentication", "infrastructure" }.Any(words.Contains)
            || new[] { "offline", "sync", "authentication" }.Any(domain => HasDomain(route, domain)));
        var behavior = !documentation && explicitCodeSignal;

        var standards = new List<EngineeringStandardExpectation>
        {
            E("acceptanceCriteria", StandardApplicability.Required, "Every task must satisfy its confirmed acceptance criteria."),
            E("architecture", documentation ? StandardApplicability.NotApplicable : StandardApplicability.Required, documentation ? "No executable architecture is changed." : "Keep dependencies inward and boundaries replaceable."),
            E("solid", documentation ? StandardApplicability.NotApplicable : StandardApplicability.Required, documentation ? "No code design is changed." : "Apply SOLID pragmatically without ceremonial abstractions."),
            E("cleanCode", documentation ? StandardApplicability.NotApplicable : StandardApplicability.Required, documentation ? "No executable code is changed." : "Prefer focused, readable, scope-disciplined code."),
            E("tddTestsFirst", behavior ? StandardApplicability.Required : StandardApplicability.NotApplicable, behavior ? "Use RED-GREEN-REFACTOR where practical." : "The task does not change executable behavior."),
            E("unitTests", behavior ? StandardApplicability.Required : StandardApplicability.NotApplicable, behavior ? "Behavior and failure paths require focused tests." : "No testable behavior is changed."),
            E("integrationTests", boundary ? StandardApplicability.Required : documentation ? StandardApplicability.NotApplicable : StandardApplicability.Consider, boundary ? "A meaningful external or persistence boundary is affected." : documentation ? "No integration boundary is changed." : "Required only if the implementation crosses a meaningful boundary."),
            E("regressionTests", bug ? StandardApplicability.Required : StandardApplicability.NotApplicable, bug ? "Reproduce the bug in an automated test before fixing it where practical." : "The task is not classified as a bug fix."),
            E("security", documentation ? StandardApplicability.Consider : StandardApplicability.Required, "Preserve repository and product security boundaries."),
            E("scope", StandardApplicability.Required, "Avoid unrelated behavior or refactoring."),
        };

        foreach (var category in FlutterUiReviewCategories)
            standards.Add(E(category, flutterUi ? StandardApplicability.Required : StandardApplicability.NotApplicable,
                flutterUi ? FlutterRationale(category) : "The task is not a Flutter UI task."));

        var validation = new List<EngineeringStandardExpectation>
        {
            E("Build", documentation ? StandardApplicability.NotApplicable : flutter ? StandardApplicability.Consider : StandardApplicability.Required, flutter ? "Use the project's Flutter build checks when configured and applicable." : "Run dotnet build or the project's equivalent compiler."),
            E("UnitTests", behavior ? StandardApplicability.Required : StandardApplicability.NotApplicable, flutter ? "Run flutter test; written tests are not considered passing until this succeeds." : "Run dotnet test or the project's equivalent unit suite."),
            E("IntegrationTests", boundary ? StandardApplicability.Required : documentation ? StandardApplicability.NotApplicable : StandardApplicability.Consider, flutter ? "Run flutter test integration_test when applicable infrastructure exists." : "Run the applicable integration-test project for changed boundaries."),
            E("Format", documentation ? StandardApplicability.Consider : StandardApplicability.Required, flutter ? "Run the configured Dart formatter verification." : "Run dotnet format or the project's equivalent formatter check."),
            E("StaticAnalysis", documentation ? StandardApplicability.NotApplicable : StandardApplicability.Required, flutter ? "Run flutter analyze." : "Run configured compiler/static-analysis checks."),
            E("ResponsiveWidgetTests", flutterUi ? StandardApplicability.Required : StandardApplicability.NotApplicable, "Run responsive widget tests across representative viewport classes."),
        };
        return new EngineeringProfile(taskKind, !documentation && explicitCodeSignal, flutter, flutterUi, standards, validation);
    }

    private static EngineeringStandardExpectation E(string category, StandardApplicability applicability, string rationale) => new(category, applicability, rationale);

    private static bool HasDomain(RoutingResult route, string domain) => route.Domains.Contains(domain, StringComparer.OrdinalIgnoreCase);

    private static string FlutterRationale(string category) => category switch
    {
        "responsiveDesign" => "Use available constraints across compact, medium, and expanded viewports.",
        "adaptiveLayout" => "Use composition changes when device-class usability benefits.",
        "overflowSafety" => "Supported viewports must not clip or overflow important content.",
        "textScaling" => "Important content and actions must survive reasonable text scaling.",
        "localizationLayoutSafety" => "Translated and dynamic content must not depend on fixed text lengths.",
        "keyboardSafety" => "Forms must keep fields and actions reachable with the keyboard open.",
        "designSystemCompliance" => "Reuse centralized tokens, breakpoints, and components.",
        "responsiveTests" => "Test meaningful small-phone, phone, large-phone, and tablet equivalents.",
        "prototypeConformity" => "Compare the rendered authoritative prototype state with Flutter emulator/Patrol evidence; preserve product structure and intent.",
        _ => "Accessibility and responsive behavior must be evaluated together."
    };
}
