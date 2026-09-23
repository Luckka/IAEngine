using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Hosting;
using OnlineOs.AiOrchestrator.Infrastructure;
using OnlineOs.AiOrchestrator.Models;

namespace OnlineOs.AiOrchestrator.Tests;

public sealed class EngineHostTests
{
    [Fact]
    public async Task GenericConsumerHostResolvesAndExecutesWithLocalFakes()
    {
        var workspace = Directory.CreateTempSubdirectory("iaengine-host-");
        try
        {
            var options = Options(workspace.FullName);
            var composition = new EngineCompositionPlan(
                "infra-sentinel",
                false,
                false,
                false,
                false,
                false,
                [],
                ["local-router", "local-implementation", "local-review"],
                [],
                ["local-policy"],
                ["validation"]);

            var host = EngineHost.Create(new EngineHostContext
            {
                ProjectId = "infra-sentinel",
                WorkspaceRoot = workspace.FullName,
                Options = options,
                Composition = composition,
                Components = new("local-router", "local-implementation", "local-review", "validation"),
                RegisterComponents = builder => builder
                    .RegisterProvider<ITaskRouter>("local-router", () => new FakeRouter())
                    .RegisterProvider<IImplementationAgent>("local-implementation", () => new FakeImplementation())
                    .RegisterProvider<IReviewAgent>("local-review", () => new FakeReviewer())
                    .RegisterPolicy("local-policy", () => new NamedProjectPolicyComponent("local-policy"))
                    .RegisterCapability<IValidationRunner>("validation", () => new FakeValidation()),
                RunStore = new RunStore(workspace.FullName, ".ai-runs"),
                Git = new FakeGit(workspace.FullName)
            });

            var result = await host.RunAsync(new DevelopmentTask("SYN-001", "Synthetic local task", "Validate host execution", "feature"));

            Assert.True(result.Succeeded);
            Assert.Equal(WorkflowState.Approved, result.State);
            Assert.Contains(host.Runtime.ResolvedComponents, x => x is { Kind: ProjectComponentKind.Capability, Name: "validation" });
            Assert.True(File.Exists(Path.Combine(workspace.FullName, ".ai-runs", result.RunId, "run.json")));
        }
        finally
        {
            workspace.Delete(true);
        }
    }

    [Fact]
    public void MissingValidationCapabilityFailsBeforeConsumerFactoriesRun()
    {
        var workspace = Directory.CreateTempSubdirectory("iaengine-host-");
        try
        {
            var options = Options(workspace.FullName);
            var composition = new EngineCompositionPlan(
                "infra-sentinel", false, false, false, false, false, [], ["local-router"], [], [], ["validation"]);
            var factoryCalled = false;

            var exception = Assert.Throws<CompositionResolutionException>(() => EngineHost.Create(new EngineHostContext
            {
                ProjectId = "infra-sentinel",
                WorkspaceRoot = workspace.FullName,
                Options = options,
                Composition = composition,
                Components = new("local-router", "local-implementation", "local-review", "validation"),
                RegisterComponents = builder => builder.RegisterProvider<ITaskRouter>("local-router", () =>
                {
                    factoryCalled = true;
                    return new FakeRouter();
                }),
                RunStore = new RunStore(workspace.FullName, ".ai-runs"),
                Git = new FakeGit(workspace.FullName)
            }));

            Assert.Contains(exception.MissingComponents, x => x is { Kind: ProjectComponentKind.Capability, Name: "validation" });
            Assert.False(factoryCalled);
            Assert.False(Directory.Exists(Path.Combine(workspace.FullName, ".ai-runs")));
        }
        finally
        {
            workspace.Delete(true);
        }
    }

    private static AppOptions Options(string workspace) => new()
    {
        Project = new ProjectProfileOptions { Id = "infra-sentinel", WorkspaceRoot = workspace, Stack = "dotnet" },
        Orchestrator = new OrchestratorOptions { RunsDirectory = ".ai-runs", ProcessTimeoutSeconds = 30 },
        ReviewPolicy = new ReviewPolicyOptions()
    };

    private sealed class FakeRouter : ITaskRouter
    {
        public Task<RoutingResult> RouteAsync(DevelopmentTask task, CancellationToken cancellationToken = default)
            => Task.FromResult(new RoutingResult("feature", ["synthetic"], "low", "low", [], [], "local", "fake", true));

        public Task<(bool Reachable, bool ModelAvailable, string Detail)> CheckHealthAsync(CancellationToken cancellationToken = default)
            => Task.FromResult((true, true, "local fake"));
    }

    private sealed class FakeImplementation : IImplementationAgent
    {
        public Task<ImplementationResult> ImplementAsync(DevelopmentTask task, RoutingResult route, EngineeringProfile engineering, CancellationToken cancellationToken = default)
            => Task.FromResult(new ImplementationResult(true, "local fake implementation", []));

        public Task<ImplementationResult> RemediateAsync(DevelopmentTask task, EngineeringProfile engineering, IReadOnlyList<ValidationResult> validation, ReviewResult? review, CancellationToken cancellationToken = default, int contextReductionAttempt = 0)
            => Task.FromResult(new ImplementationResult(true, "local fake remediation", []));
    }

    private sealed class FakeReviewer : IReviewAgent
    {
        public Task<ReviewResult> ReviewAsync(DevelopmentTask task, EngineeringProfile engineering, string gitDiff, CancellationToken cancellationToken = default)
            => Task.FromResult(new ReviewResult(ReviewDecision.Pass, [], "local fake review"));
    }

    private sealed class FakeValidation : IValidationRunner
    {
        public Task<IReadOnlyList<ValidationResult>> RunAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ValidationResult>>([new("synthetic", true, new ProcessResult("fake-validation", 0, "", "", TimeSpan.Zero))]);
    }

    private sealed class FakeGit(string root) : IGitService
    {
        public Task<string> GetRootAsync(CancellationToken cancellationToken = default) => Task.FromResult(root);
        public Task<string> GetBranchAsync(CancellationToken cancellationToken = default) => Task.FromResult("feature/local-host");
        public Task<string> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task<string> GetDiffAsync(CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task<string> GetCommitAsync(CancellationToken cancellationToken = default) => Task.FromResult("local-fake");
        public Task<(bool Safe, string Reason)> CheckBranchSafetyAsync(CancellationToken cancellationToken = default)
            => Task.FromResult((true, "local fake branch"));
    }
}
