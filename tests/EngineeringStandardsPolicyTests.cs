using OnlineOs.AiOrchestrator.Agents;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Pipeline;

namespace OnlineOs.AiOrchestrator.Tests;

public sealed class EngineeringStandardsPolicyTests
{
    [Fact]
    public void FlutterMilestoneOneFoundationIsCodeFlutterAndUi()
    {
        var task = new DevelopmentTask(
            "ONLINEOS-M1",
            "Implement Milestone 1 - Flutter Technical Foundation for OnlineOS Mobile",
            "Create Flutter bootstrap, Clean Architecture structure, dependency injection, Design System, responsive/adaptive layouts, localization, go_router, Drift and automated tests. Run flutter analyze and flutter test.",
            "feature",
            AcceptanceCriteria: ["Flutter application bootstrap", "responsive/adaptive layouts", "unit/widget/infrastructure tests"]);
        var route = new RoutingResult("feature", ["architecture", "ui", "design", "testing"], "high", "high", ["ai/skills/flutter-feature/SKILL.md"], ["architecture/ARCHITECTURE.md", "docs/DESIGN_SYSTEM.md"], "claude-codex", "foundation");

        var profile = EngineeringStandardsPolicy.Create(task, route);

        Assert.True(profile.IsCodeTask);
        Assert.True(profile.IsFlutterTask);
        Assert.True(profile.IsFlutterUiTask);
        AssertRequired(profile, "architecture", "solid", "cleanCode", "tddTestsFirst", "unitTests", "responsiveDesign", "adaptiveLayout", "overflowSafety", "textScaling", "localizationLayoutSafety", "designSystemCompliance", "responsiveTests", "accessibility", "prototypeConformity");
        Assert.Equal(StandardApplicability.Required, Validation(profile, "UnitTests"));
        Assert.Equal(StandardApplicability.Required, Validation(profile, "StaticAnalysis"));
        Assert.Equal(StandardApplicability.Required, Validation(profile, "ResponsiveWidgetTests"));
    }

    [Fact]
    public void FeatureTypeAloneMakesExecutableTaskACodeTask()
    {
        var profile = Profile("feature", "Implement a foundation", ["general"]);

        Assert.True(profile.IsCodeTask);
    }

    [Fact]
    public void CodingTaskRequiresArchitectureCleanCodeAndUnitTests()
    {
        var profile = Profile("feature", "Implement service behavior", ["service-order"]);

        AssertRequired(profile, "architecture", "solid", "cleanCode", "unitTests", "tddTestsFirst");
        var prompt = ClaudeAgent.BuildImplementationPrompt(TestData.Task(), TestData.Route(), profile);
        Assert.Contains("RED-GREEN-REFACTOR", prompt);
        Assert.Contains("architecture/ENGINEERING_STANDARDS.md", prompt);
    }

    [Fact]
    public void DocumentationTaskMarksCodingAndResponsiveStandardsNotApplicable()
    {
        var task = new DevelopmentTask("DOC-1", "Update documentation", "Clarify the guide", "documentation");
        var route = Route("documentation", ["documentation"]);
        var profile = EngineeringStandardsPolicy.Create(task, route);

        AssertNotApplicable(profile, "architecture", "solid", "unitTests", "integrationTests", "responsiveDesign", "responsiveTests");
        var prompt = ClaudeAgent.BuildImplementationPrompt(task, route, profile);
        Assert.Contains("non-code task", prompt);
        Assert.DoesNotContain("Add behavior-focused unit tests", prompt);
    }

    [Fact]
    public void BugFixRequiresRegressionFirstGuidance()
    {
        var task = new DevelopmentTask("BUG-1", "Fix sync retry bug", "Correct duplicate retry behavior", "bug");
        var route = Route("bug", ["sync"]);
        var profile = EngineeringStandardsPolicy.Create(task, route);

        AssertRequired(profile, "regressionTests");
        Assert.Contains("regression-first coverage", ClaudeAgent.BuildImplementationPrompt(task, route, profile));
    }

    [Fact]
    public void InfrastructureTaskRequiresIntegrationTestConsideration()
    {
        var profile = Profile("feature", "Implement HTTP API repository", ["authentication"]);
        AssertRequired(profile, "integrationTests");
        Assert.Equal(StandardApplicability.Required, Validation(profile, "IntegrationTests"));
    }

    [Fact]
    public void FlutterUiTaskRequiresResponsiveAdaptiveAndResilienceStandards()
    {
        var profile = Profile("feature", "Implement Flutter service-order form screen", ["ui", "design"]);

        Assert.True(profile.IsFlutterUiTask);
        AssertRequired(profile, "responsiveDesign", "adaptiveLayout", "overflowSafety", "textScaling",
            "localizationLayoutSafety", "keyboardSafety", "designSystemCompliance", "responsiveTests", "accessibility");
        var prompt = ClaudeAgent.BuildImplementationPrompt(TestData.Task(), TestData.Route(), profile);
        Assert.Contains("compact/medium/expanded", prompt);
        Assert.Contains("localized content", prompt);
        Assert.Contains("keyboard-safe", prompt);
    }

    [Fact]
    public void FlutterNonUiTaskDoesNotRequireResponsiveChecks()
    {
        var profile = Profile("feature", "Implement Flutter offline sync use case", ["offline", "sync"]);

        Assert.True(profile.IsFlutterTask);
        Assert.False(profile.IsFlutterUiTask);
        AssertNotApplicable(profile, "responsiveDesign", "adaptiveLayout", "responsiveTests", "overflowSafety");
    }

    [Fact]
    public void BackendFeatureIsCodeButNotFlutter()
    {
        var profile = Profile("feature", "Implement backend HTTP API", ["security"]);

        Assert.True(profile.IsCodeTask);
        Assert.False(profile.IsFlutterTask);
        Assert.False(profile.IsFlutterUiTask);
    }

    [Fact]
    public void DocumentationDoesNotReceiveFlutterValidationExpectations()
    {
        var task = new DevelopmentTask("DOC-1", "Document Flutter setup", "Explain the setup steps", "documentation");
        var route = Route("documentation", ["documentation"]);
        var profile = EngineeringStandardsPolicy.Create(task, route);

        Assert.False(profile.IsCodeTask);
        Assert.False(profile.IsFlutterTask);
        Assert.All(profile.ExpectedValidation.Where(x => x.Category is "StaticAnalysis" or "UnitTests" or "ResponsiveWidgetTests"), x => Assert.Equal(StandardApplicability.NotApplicable, x.Applicability));
    }

    [Fact]
    public void CodexPromptContainsGeneralAndFlutterQualityRubrics()
    {
        var profile = Profile("feature", "Implement Flutter responsive screen", ["ui"]);
        var prompt = CodexAgent.BuildReviewPrompt(TestData.Task(), profile, "diff");

        Assert.Contains("acceptanceCriteria", prompt);
        Assert.Contains("architecture", prompt);
        Assert.Contains("integrationTests", prompt);
        Assert.Contains("responsiveDesign", prompt);
        Assert.Contains("overflowSafety", prompt);
        Assert.Contains("localizationLayoutSafety", prompt);
        Assert.Contains("responsiveTests", prompt);
        Assert.Contains("PASS|FAIL|NOT_APPLICABLE", prompt);
    }

    private static EngineeringProfile Profile(string type, string description, IReadOnlyList<string> domains)
    {
        var task = new DevelopmentTask("TASK-1", description, description, type, domains);
        return EngineeringStandardsPolicy.Create(task, Route(type, domains));
    }

    private static RoutingResult Route(string type, IReadOnlyList<string> domains) =>
        new(type, domains, "medium", "medium", [], ["architecture/ENGINEERING_STANDARDS.md"], "claude-codex", "test");

    private static void AssertRequired(EngineeringProfile profile, params string[] categories)
    {
        foreach (var category in categories)
            Assert.Equal(StandardApplicability.Required, Standard(profile, category));
    }

    private static void AssertNotApplicable(EngineeringProfile profile, params string[] categories)
    {
        foreach (var category in categories)
            Assert.Equal(StandardApplicability.NotApplicable, Standard(profile, category));
    }

    private static StandardApplicability Standard(EngineeringProfile profile, string category) =>
        profile.Standards.Single(x => x.Category == category).Applicability;

    private static StandardApplicability Validation(EngineeringProfile profile, string category) =>
        profile.ExpectedValidation.Single(x => x.Category == category).Applicability;
}
