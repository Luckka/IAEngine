using System.Text.Json;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Agents;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Pipeline;

namespace OnlineOs.AiOrchestrator.Tests;

public sealed class CodexAgentTests
{
    [Fact]
    public async Task ParsesCompleteStructuredQualityReviewIncludingNotApplicable()
    {
        var task = new DevelopmentTask("DOC-1", "Update docs", "Documentation only", "documentation");
        var route = new RoutingResult("documentation", ["documentation"], "low", "low", [], [], "local-only", "test");
        var profile = EngineeringStandardsPolicy.Create(task, route);
        var response = JsonSerializer.Serialize(new
        {
            decision = "PASS",
            findings = Array.Empty<object>(),
            quality = profile.Standards.Select(x => new
            {
                category = x.Category,
                status = x.Applicability == StandardApplicability.NotApplicable ? "NOT_APPLICABLE" : "PASS",
                summary = "evaluated",
                blocking = false
            }),
            summary = "review complete"
        });
        var agent = CreateAgent(response);

        var result = await agent.ReviewAsync(task, profile, "diff");

        Assert.Equal(ReviewDecision.Pass, result.Decision);
        Assert.Equal(profile.Standards.Count, result.Quality!.Count);
        Assert.Contains(result.Quality, x => x.Status == QualityStatus.NotApplicable);
    }

    [Fact]
    public async Task MissingEngineeringQualityRubricFailsReview()
    {
        var agent = CreateAgent("{\"decision\":\"PASS\",\"findings\":[],\"summary\":\"ok\"}");

        var result = await agent.ReviewAsync(TestData.Task(), TestData.Engineering(), "diff");

        Assert.Equal(ReviewDecision.Fail, result.Decision);
        Assert.Contains("malformed structured output", result.Summary);
    }

    [Fact]
    public async Task RedundantDeterministicCategoriesAreIgnored()
    {
        var profile = TestData.Engineering();
        var quality = profile.Standards.Select(x => Assessment(x.Category, x.Applicability)).Cast<object>()
            .Concat([Assessment("Format", StandardApplicability.Required), Assessment("StaticAnalysis", StandardApplicability.Required)])
            .ToArray();
        var result = await CreateAgent(Response(quality)).ReviewAsync(TestData.Task(), profile,
            [TestData.Validation(false) with { Category = "Format" }], "diff");

        Assert.False(result.IsProtocolFailure);
        Assert.Equal(profile.Standards.Count, result.Quality!.Count);
        Assert.DoesNotContain(result.Quality, x => x.Category is "Format" or "StaticAnalysis");
    }

    [Fact]
    public async Task MissingStandardFailsProviderProtocol()
    {
        var profile = TestData.Engineering();
        var result = await CreateAgent(Response(profile.Standards.Skip(1).Select(x => Assessment(x.Category, x.Applicability))))
            .ReviewAsync(TestData.Task(), profile, "diff");

        Assert.True(result.IsProtocolFailure);
    }

    [Fact]
    public async Task DuplicateStandardFailsProviderProtocol()
    {
        var profile = TestData.Engineering();
        var quality = profile.Standards.Select(x => Assessment(x.Category, x.Applicability)).Cast<object>()
            .Append(Assessment(profile.Standards[0].Category, profile.Standards[0].Applicability));
        var result = await CreateAgent(Response(quality)).ReviewAsync(TestData.Task(), profile, "diff");

        Assert.True(result.IsProtocolFailure);
    }

    [Fact]
    public async Task UnknownExtraCategoryFailsProviderProtocol()
    {
        var profile = TestData.Engineering();
        var quality = profile.Standards.Select(x => Assessment(x.Category, x.Applicability)).Cast<object>()
            .Append(Assessment("inventedCategory", StandardApplicability.Required));
        var result = await CreateAgent(Response(quality)).ReviewAsync(TestData.Task(), profile, "diff");

        Assert.True(result.IsProtocolFailure);
    }

    [Fact]
    public async Task LegitimateFailFindingsSurviveNormalization()
    {
        var profile = TestData.Engineering();
        var quality = profile.Standards.Select(x => Assessment(x.Category,
            x.Category == "architecture" ? StandardApplicability.Required : x.Applicability,
            x.Category == "architecture" ? "REQ-UX-004 side-rail navigation is omitted" : "evaluated", x.Category == "architecture"));
        var response = JsonSerializer.Serialize(new
        {
            decision = "FAIL",
            findings = new[] { new { severity = "HIGH", file = "app/lib/router.dart", location = "side rail", problem = "REQ-UX-004 side-rail navigation is omitted", impact = "Tablet navigation is incomplete.", recommendation = "Restore the side rail.", blocking = true } },
            quality,
            summary = "legitimate engineering failure"
        });

        var result = await CreateAgent(response).ReviewAsync(TestData.Task(), profile, "diff");

        Assert.Equal(ReviewDecision.Fail, result.Decision);
        Assert.Contains(result.Findings, x => x.Problem.Contains("side-rail navigation"));
        Assert.False(new ReviewPolicy(new Configuration.ReviewPolicyOptions()).Passes(result));
    }

    private static object Assessment(string category, StandardApplicability applicability, string summary = "evaluated", bool blocking = false)
        => new { category, status = applicability == StandardApplicability.NotApplicable ? "NOT_APPLICABLE" : "PASS", summary, blocking };

    private static string Response(IEnumerable<object> quality) => JsonSerializer.Serialize(new
    {
        decision = "PASS",
        findings = Array.Empty<object>(),
        quality = quality.ToArray(),
        summary = "review complete"
    });

    private static CodexAgent CreateAgent(string output) => new(
        new StubProcessRunner(new ProcessResult("codex", 0, output, "", TimeSpan.Zero)),
        new CliAgentOptions { Command = "codex", Arguments = ["exec", "-"] },
        "/repo",
        TimeSpan.FromMinutes(1));

    private sealed class StubProcessRunner(ProcessResult result) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken = default) => Task.FromResult(result);
    }
}
