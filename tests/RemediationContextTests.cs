using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Pipeline;

namespace OnlineOs.AiOrchestrator.Tests;

public sealed class RemediationContextTests
{
    [Fact]
    public void FocusedContextRetainsFindingRequirementAndTargetWithoutHistory()
    {
        var task = TestData.Task() with { AcceptanceCriteria = ["REQ-OS-017: each block exposes progress and pending reason"] };
        var review = new ReviewResult(ReviewDecision.Fail,
            [new ReviewFinding(FindingSeverity.High, "app/lib/service_order_hub_content.dart", "34-44", "REQ-OS-017 progress is missing", "users cannot see state", "expose read-only state")], "review");
        var package = new RemediationContextBuilder(new RemediationContextOptions { FocusedBudgetTokens = 200 }, "/repo")
            .Build(task, TestData.Engineering(task), [], review, 0);

        Assert.Contains("REQ-OS-017", package.Prompt);
        Assert.Contains("service_order_hub_content.dart", package.Prompt);
        Assert.DoesNotContain("old diagnosis", package.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("focused", package.Strategy);
    }

    [Fact]
    public void MinimalReductionIsSmallerAndDeduplicatesTargetFiles()
    {
        var repository = Directory.CreateTempSubdirectory("orchestrator-context-");
        File.WriteAllText(Path.Combine(repository.FullName, "missing.dart"), "synthetic target");
        var task = TestData.Task() with { Description = new string('x', 30_000) };
        var review = new ReviewResult(ReviewDecision.Fail,
            [new ReviewFinding(FindingSeverity.High, "missing.dart", "1", "finding", "impact", "fix"),
             new ReviewFinding(FindingSeverity.High, "missing.dart", "2", "duplicate", "impact", "fix")], "review");
        var options = new RemediationContextOptions { FocusedBudgetTokens = 2_000, MinimalBudgetTokens = 1_000 };
        var builder = new RemediationContextBuilder(options, repository.FullName);
        var focused = builder.Build(task, TestData.Engineering(task), [], review, 0);
        var minimal = builder.Build(task, TestData.Engineering(task), [], review, 1);

        Assert.True(minimal.EstimatedTokens < focused.EstimatedTokens);
        Assert.Single(minimal.IncludedFiles);
        repository.Delete(true);
    }
}
