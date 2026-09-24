using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Hosting;
using OnlineOs.AiOrchestrator.Infrastructure;
using OnlineOs.AiOrchestrator.Models;

namespace IAEngine.Core.Tests;

public sealed class CoreHostCompositionTests
{
    [Fact]
    public async Task CoreHostExecutesWithConsumerOwnedFakes()
    {
        var workspace = Directory.CreateTempSubdirectory("iaengine-core-");
        try
        {
            var options = new AppOptions
            {
                Project = new ProjectProfileOptions { Id = "core-test", WorkspaceRoot = workspace.FullName, Stack = "generic" },
                Orchestrator = new OrchestratorOptions { RunsDirectory = ".ai-runs-core", ProcessTimeoutSeconds = 30 },
                ReviewPolicy = new ReviewPolicyOptions()
            };
            var composition = new EngineCompositionPlan(
                "core-test", false, false, false, false, false, [],
                ["router", "implementation", "review"], [], ["policy"], ["validation"]);
            var host = EngineHost.Create(new EngineHostContext
            {
                ProjectId = "core-test",
                WorkspaceRoot = workspace.FullName,
                Options = options,
                Composition = composition,
                Components = new("router", "implementation", "review", "validation"),
                RegisterComponents = builder => builder
                    .RegisterProvider<ITaskRouter>("router", () => new FakeRouter())
                    .RegisterProvider<IImplementationAgent>("implementation", () => new FakeImplementation())
                    .RegisterProvider<IReviewAgent>("review", () => new FakeReview())
                    .RegisterPolicy("policy", () => new NamedProjectPolicyComponent("policy"))
                    .RegisterCapability<IValidationRunner>("validation", () => new FakeValidation()),
                RunStore = new RunStore(workspace.FullName, ".ai-runs-core"),
                Git = new FakeGit(workspace.FullName)
            });

            var result = await host.RunAsync(new DevelopmentTask("CORE-001", "Core task", "Execute locally", "feature"));

            Assert.True(result.Succeeded);
            Assert.Equal(WorkflowState.Approved, result.State);
            Assert.True(File.Exists(Path.Combine(workspace.FullName, ".ai-runs-core", result.RunId, "run.json")));
        }
        finally
        {
            workspace.Delete(true);
        }
    }

    private sealed class FakeRouter : ITaskRouter
    {
        public Task<RoutingResult> RouteAsync(DevelopmentTask task, CancellationToken cancellationToken = default)
            => Task.FromResult(new RoutingResult("feature", ["generic"], "low", "low", [], [], "local", "fake", true));

        public Task<(bool Reachable, bool ModelAvailable, string Detail)> CheckHealthAsync(CancellationToken cancellationToken = default)
            => Task.FromResult((true, true, "local fake"));
    }

    private sealed class FakeImplementation : IImplementationAgent
    {
        public Task<ImplementationResult> ImplementAsync(DevelopmentTask task, RoutingResult route, EngineeringProfile engineering, CancellationToken cancellationToken = default)
            => Task.FromResult(new ImplementationResult(true, "fake", []));

        public Task<ImplementationResult> RemediateAsync(DevelopmentTask task, EngineeringProfile engineering, IReadOnlyList<ValidationResult> validation, ReviewResult? review, CancellationToken cancellationToken = default, int contextReductionAttempt = 0)
            => Task.FromResult(new ImplementationResult(true, "fake", []));
    }

    private sealed class FakeReview : IReviewAgent
    {
        public Task<ReviewResult> ReviewAsync(DevelopmentTask task, EngineeringProfile engineering, string gitDiff, CancellationToken cancellationToken = default)
            => Task.FromResult(new ReviewResult(ReviewDecision.Pass, [], "fake"));
    }

    private sealed class FakeValidation : IValidationRunner
    {
        public Task<IReadOnlyList<ValidationResult>> RunAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ValidationResult>>([new("generic", true, new ProcessResult("fake", 0, "", "", TimeSpan.Zero))]);
    }

    private sealed class FakeGit(string root) : IGitService
    {
        public Task<string> GetRootAsync(CancellationToken cancellationToken = default) => Task.FromResult(root);
        public Task<string> GetBranchAsync(CancellationToken cancellationToken = default) => Task.FromResult("feature/core-test");
        public Task<string> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task<string> GetDiffAsync(CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task<string> GetCommitAsync(CancellationToken cancellationToken = default) => Task.FromResult("core-test");
        public Task<(bool Safe, string Reason)> CheckBranchSafetyAsync(CancellationToken cancellationToken = default)
            => Task.FromResult((true, "local fake"));
    }
}
