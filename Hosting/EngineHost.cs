using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Pipeline;
using RoadmapMilestoneDefinition = OnlineOs.AiOrchestrator.Roadmap.MilestoneDefinition;

namespace OnlineOs.AiOrchestrator.Hosting;

/// <summary>
/// Generic execution host. The consumer owns configuration, component factories,
/// workspace services and persistence; the Engine owns runtime resolution and the
/// existing workflow executor.
/// </summary>
public sealed class EngineHost
{
    private readonly EngineHostContext context;
    private readonly EngineCompositionRuntime runtime;
    private readonly Orchestrator orchestrator;

    private EngineHost(EngineHostContext context, EngineCompositionRuntime runtime, Orchestrator orchestrator)
    {
        this.context = context;
        this.runtime = runtime;
        this.orchestrator = orchestrator;
    }

    public string ProjectId => context.ProjectId;
    public string WorkspaceRoot => context.WorkspaceRoot;
    public EngineCompositionRuntime Runtime => runtime;

    public static EngineHost Create(EngineHostContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ValidateContext(context);

        var builder = context.Composition.CreateRuntimeBuilder();
        context.RegisterComponents(builder);
        var resolved = builder.Build();
        var names = context.Components;

        var router = resolved.ResolveProvider<ITaskRouter>(names.RouterProvider);
        var implementation = resolved.ResolveProvider<IImplementationAgent>(names.ImplementationProvider);
        var reviewer = resolved.ResolveProvider<IReviewAgent>(names.ReviewProvider);
        var validation = resolved.ResolveCapability<IValidationRunner>(names.ValidationCapability);

        var hostOrchestrator = new Orchestrator(
            router,
            implementation,
            validation,
            reviewer,
            context.Git,
            context.RunStore,
            new ReviewPolicy(context.Options.ReviewPolicy),
            new WorkflowStateMachine(),
            context.Options,
            context.ProgressReporter,
            router as IFailureDiagnoser,
            context.ReferenceInspector,
            context.ExpectedBranch);

        return new EngineHost(context, resolved, hostOrchestrator);
    }

    public Task<EngineExecutionResult> RunAsync(
        DevelopmentTask task,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(task, dryRun, cancellationToken);

    public async Task<EngineExecutionResult> RunTaskAsync(
        string taskId,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        if (context.TaskSource is null)
            throw new InvalidOperationException("No task source was supplied by the consumer.");
        return await ExecuteAsync(await context.TaskSource.LoadTaskAsync(taskId, cancellationToken), dryRun, cancellationToken);
    }

    public Task<RoadmapMilestoneDefinition> LoadMilestoneAsync(
        string milestoneId,
        CancellationToken cancellationToken = default)
    {
        if (context.MilestoneSource is null)
            throw new InvalidOperationException("No milestone source was supplied by the consumer.");
        return context.MilestoneSource.LoadMilestoneAsync(milestoneId, cancellationToken);
    }

    private async Task<EngineExecutionResult> ExecuteAsync(DevelopmentTask task, bool dryRun, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(task);
        var result = await orchestrator.ExecuteAsync(task, dryRun, ct);
        var run = result.Run;
        var succeeded = run.State is WorkflowState.Approved or WorkflowState.Completed
            && (run.FinalDecision is "PASS" or "DRY_RUN" or "REFERENCE_VALIDATED");
        return new EngineExecutionResult(
            context.ProjectId,
            run.RunId,
            run.State,
            run.FinalDecision,
            succeeded,
            run.LastFailure?.RootCause);
    }

    private static void ValidateContext(EngineHostContext context)
    {
        if (string.IsNullOrWhiteSpace(context.ProjectId))
            throw new ArgumentException("Project id is required.", nameof(context));
        if (string.IsNullOrWhiteSpace(context.WorkspaceRoot) || !Directory.Exists(context.WorkspaceRoot))
            throw new ArgumentException("Workspace root must exist.", nameof(context));
        if (!string.Equals(context.ProjectId, context.Composition.ProjectId, StringComparison.Ordinal))
            throw new InvalidOperationException("Host project id must match the declared composition project id.");
        if (!string.Equals(context.ProjectId, context.Options.Project.Id, StringComparison.Ordinal))
            throw new InvalidOperationException("Host project id must match the project profile id.");

        var errors = ProjectProfileValidator.Validate(context.Options.Project);
        if (errors.Count > 0)
            throw new CompositionResolutionException(
                $"Project '{context.ProjectId}' configuration is invalid: {string.Join(" ", errors)}", []);
    }
}
