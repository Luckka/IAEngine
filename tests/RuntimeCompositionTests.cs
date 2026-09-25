using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Reference;

namespace OnlineOs.AiOrchestrator.Tests;

public sealed class RuntimeCompositionTests
{
    [Fact]
    public void GenericCompositionResolvesOnlyExplicitLocalFakes()
    {
        var plan = new EngineCompositionPlan(
            "generic-consumer",
            false,
            true,
            false,
            false,
            false,
            ["docs/generic"],
            ["local-router"],
            ["local-validation"],
            ["generic-policy"],
            ["validation"]);
        var router = new FakeRouter();
        var validation = new FakeValidation();

        var runtime = plan.CreateRuntimeBuilder()
            .RegisterProvider<ITaskRouter>("local-router", () => router)
            .RegisterValidator<IValidationRunner>("local-validation", () => validation)
            .RegisterPolicy("generic-policy", () => new NamedProjectPolicyComponent("generic-policy"))
            .RegisterCapability<IValidationRunner>("validation", () => validation)
            .Build();

        Assert.DoesNotContain(runtime.DeclaredComponents, x => x.Name.Contains("onlineos", StringComparison.OrdinalIgnoreCase));
        Assert.Same(router, runtime.ResolveProvider<ITaskRouter>("local-router"));
        Assert.Same(validation, runtime.ResolveValidator<IValidationRunner>("local-validation"));
        Assert.Same(validation, runtime.ResolveCapability<IValidationRunner>("validation"));
        Assert.Equal("generic-policy", runtime.ResolvePolicy("generic-policy").Name);
        Assert.All(runtime.Statuses, status => Assert.True(status.Registered && status.Resolved));
    }

    [Fact]
    public void UndeclaredRegistrationIsNotInstantiatedOrResolved()
    {
        var plan = new EngineCompositionPlan(
            "generic-consumer", false, false, false, false, false, [], [], [], [], []);
        var instantiated = false;

        var runtime = plan.CreateRuntimeBuilder()
            .RegisterProvider<ITaskRouter>("onlineos-router", () =>
            {
                instantiated = true;
                return new FakeRouter();
            })
            .Build();

        Assert.Contains(runtime.RegisteredComponents, x => x.Name == "onlineos-router");
        Assert.Empty(runtime.ResolvedComponents);
        Assert.False(instantiated);
        Assert.Throws<CompositionResolutionException>(() => runtime.ResolveProvider<ITaskRouter>("onlineos-router"));
    }

    [Fact]
    public void MissingDeclaredRegistrationFailsBeforeAnyFactoryRuns()
    {
        var plan = new EngineCompositionPlan(
            "generic-consumer", false, false, false, false, false, [], ["local-router"], [], [], ["validation"]);
        var instantiated = false;

        var exception = Assert.Throws<CompositionResolutionException>(() => plan.CreateRuntimeBuilder()
            .RegisterProvider<ITaskRouter>("local-router", () =>
            {
                instantiated = true;
                return new FakeRouter();
            })
            .Build());

        Assert.Contains(exception.MissingComponents, x => x is { Kind: ProjectComponentKind.Capability, Name: "validation" });
        Assert.Contains("Capability:validation", exception.Message, StringComparison.Ordinal);
        Assert.False(instantiated);
    }

    [Fact]
    public void OnlineOsConfigurationNamesAreExplicitlyRegisterableWithoutExecutingProviders()
    {
        var options = ConfigLoader.LoadFile(Path.Combine(RepositoryRoot(), "appsettings.onlineos.json"));
        var plan = EngineComposition.Create(options);
        var validation = new FakeValidation();
        var builder = plan.CreateRuntimeBuilder()
            .RegisterProvider<ITaskRouter>("ollama", () => new FakeRouter())
            .RegisterProvider<IImplementationAgent>("claude", () => new FakeImplementation())
            .RegisterProvider<IReviewAgent>("codex", () => new FakeReview())
            .RegisterCapability<IValidationRunner>("validation", () => validation)
            .RegisterCapability<IE2ETestRunner>("qa", () => new FakeE2E())
            .RegisterCapability<IReferenceInspector>("reference-inspection", () => new FakeReference())
            .RegisterCapability<IGitWorkflowManager>("git-workflow", () => new FakeGitWorkflow());

        foreach (var validator in options.Project.Validators)
            builder.RegisterValidator<IValidationRunner>(validator, () => validation);
        foreach (var policy in options.Project.Policies)
            builder.RegisterPolicy(policy, () => new NamedProjectPolicyComponent(policy));

        var runtime = builder.Build();

        Assert.True(plan.IsOnlineOsCompatibility);
        Assert.Contains(runtime.ResolvedComponents, x => x is { Kind: ProjectComponentKind.Provider, Name: "ollama" });
        Assert.Contains(runtime.ResolvedComponents, x => x is { Kind: ProjectComponentKind.Provider, Name: "claude" });
        Assert.Contains(runtime.ResolvedComponents, x => x is { Kind: ProjectComponentKind.Provider, Name: "codex" });
        Assert.Contains(runtime.ResolvedComponents, x => x is { Kind: ProjectComponentKind.Capability, Name: "qa" });
        Assert.Contains(runtime.ResolvedComponents, x => x is { Kind: ProjectComponentKind.Capability, Name: "reference-inspection" });
        Assert.Contains(runtime.ResolvedComponents, x => x is { Kind: ProjectComponentKind.Capability, Name: "git-workflow" });
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory?.FullName ?? Directory.GetCurrentDirectory();
    }

    private sealed class FakeRouter : ITaskRouter
    {
        public Task<RoutingResult> RouteAsync(DevelopmentTask task, CancellationToken cancellationToken = default)
            => Task.FromResult(TestData.Route());

        public Task<(bool Reachable, bool ModelAvailable, string Detail)> CheckHealthAsync(CancellationToken cancellationToken = default)
            => Task.FromResult((true, true, "fake"));
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
            => Task.FromResult<IReadOnlyList<ValidationResult>>([]);
    }

    private sealed class FakeE2E : IE2ETestRunner
    {
        public Task<E2ETestResult> RunAsync(E2ETestRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new E2ETestResult(true, 0, TimeSpan.Zero, request, [], [], [], "", "", "", ""));
    }

    private sealed class FakeReference : IReferenceInspector
    {
        public Task<BackendReferenceArtifact> InspectAsync(DevelopmentTask task, CancellationToken cancellationToken = default)
            => Task.FromResult(new BackendReferenceArtifact { TaskId = task.Id });
    }

    private sealed class FakeGitWorkflow : IGitWorkflowManager
    {
        private static GitWorkflowResult Result() => new(true, GitLifecycleState.NotPrepared, "fake", new GitLifecycleMetadata());

        public Task<GitWorkflowResult> PrepareMilestoneAsync(MilestoneDefinition milestone, string? runId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Result());

        public Task<GitWorkflowResult> FinalizeMilestoneAsync(MilestoneDefinition milestone, bool gatesPassed, string? runId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Result());

        public Task<GitLifecycleMetadata?> LoadAsync(string milestoneId, CancellationToken cancellationToken = default)
            => Task.FromResult<GitLifecycleMetadata?>(new());
    }
}
