using System.Text.Json;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Agents;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Infrastructure;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Pipeline;
using OnlineOs.AiOrchestrator.Roadmap;
using OnlineOs.AiOrchestrator.Reference;

var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
var toolDirectory = AppContext.BaseDirectory;
var repositoryCandidate = FindRepository(Directory.GetCurrentDirectory()) ?? FindRepository(toolDirectory);
if (repositoryCandidate is null)
{
    Console.Error.WriteLine("No Git repository found. Run from the OnlineOS repository or a descendant.");
    return 2;
}
var workspace = new WorkspaceBoundary(repositoryCandidate);
var repository = workspace.Root;

var configDirectory = FindConfigDirectory(Directory.GetCurrentDirectory(), toolDirectory);
AppOptions options;
try { options = ConfigLoader.Load(configDirectory); }
catch (JsonException exception)
{
    Console.Error.WriteLine($"Configuration rejected before providers or external commands were started: invalid JSON ({exception.Message}).");
    return 2;
}
var configurationErrors = ProjectProfileValidator.ValidateAppOptions(options);
if (configurationErrors.Count > 0)
{
    Console.Error.WriteLine("Configuration rejected before providers or external commands were started:");
    foreach (var error in configurationErrors) Console.Error.WriteLine($"- {error}");
    return 2;
}
var composition = EngineComposition.Create(options);
var helpRequested = args.Length == 0 || args[0] is "help" or "--help" or "-h";
if (!composition.IsOnlineOsCompatibility && !helpRequested)
{
    Console.Error.WriteLine($"Project profile '{options.Project.Id}' has no host registration for its declared runtime composition. " +
        "A consumer must register its providers, validators, policies, and capabilities explicitly before running commands.");
    return 2;
}

if (helpRequested)
{
    PrintUsage();
    return 0;
}

IProcessRunner processes = new ProcessRunner();
using var http = new HttpClient { BaseAddress = new Uri(options.Ollama.BaseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(options.Ollama.TimeoutSeconds) };
IGitService git = new GitService(processes, repository, options.Git);
var timeout = TimeSpan.FromSeconds(options.Orchestrator.ProcessTimeoutSeconds);
var onlineOsRouter = new OllamaTaskRouter(http, options.Ollama);
var onlineOsValidation = new ValidationRunner(processes, options.Validation, repository, timeout);
var onlineOsReference = new MonolithReferenceInspector(processes, repository, options.Reference);
var onlineOsGitWorkflow = new GitWorkflowManager(processes, repository, options.Git, onlineOsValidation);
var onlineOsQa = new PatrolE2ETestRunner(
    processes,
    new PatrolDeviceManager(processes, options.Qa, repository),
    options.Qa,
    repository,
    new AdbDeviceArtifactCapture(processes, options.Qa, repository),
    new AdbVideoCapture(processes, options.Qa, repository),
    new PatrolExecutableResolver(options.Qa, repository),
    Console.WriteLine);

var runtimeBuilder = composition.CreateRuntimeBuilder();
runtimeBuilder.RegisterProvider<ITaskRouter>("ollama", () => onlineOsRouter);
runtimeBuilder.RegisterProvider<IImplementationAgent>("claude", () => new ClaudeAgent(processes, options.Claude, repository, timeout, options.Orchestrator.RemediationContext));
runtimeBuilder.RegisterProvider<IReviewAgent>("codex", () => new CodexAgent(processes, options.Codex, repository, timeout));
foreach (var validator in options.Project.Validators)
    runtimeBuilder.RegisterValidator<IValidationRunner>(validator, () => onlineOsValidation);
runtimeBuilder.RegisterCapability<IValidationRunner>("validation", () => onlineOsValidation);
runtimeBuilder.RegisterCapability<IE2ETestRunner>("qa", () => onlineOsQa);
runtimeBuilder.RegisterCapability<IReferenceInspector>("reference-inspection", () => onlineOsReference);
runtimeBuilder.RegisterCapability<IGitWorkflowManager>("git-workflow", () => onlineOsGitWorkflow);
foreach (var policy in options.Project.Policies)
    runtimeBuilder.RegisterPolicy(policy, () => new NamedProjectPolicyComponent(policy));

EngineCompositionRuntime resolvedComposition;
try
{
    resolvedComposition = runtimeBuilder.Build();
}
catch (CompositionResolutionException exception)
{
    Console.Error.WriteLine($"Runtime composition rejected before providers or external commands were started: {exception.Message}");
    return 2;
}

ITaskRouter router = resolvedComposition.ResolveProvider<ITaskRouter>("ollama");
var implementationAgent = resolvedComposition.ResolveProvider<IImplementationAgent>("claude");
var reviewAgent = resolvedComposition.ResolveProvider<IReviewAgent>("codex");
IReferenceInspector referenceInspector = resolvedComposition.ResolveCapability<IReferenceInspector>("reference-inspection");
var validationRunner = resolvedComposition.ResolveCapability<IValidationRunner>("validation");
var gitWorkflow = resolvedComposition.ResolveCapability<IGitWorkflowManager>("git-workflow");
var preflight = new PreflightService(processes, router, git, options, repository);

if (args[0] == "preflight")
{
    var preflightRunId = Orchestrator.CreateRunId();
    using IProgressReporter preflightProgress = new ConsoleProgressReporter(Console.Out);
    string preflightBranch;
    try { preflightBranch = await git.GetBranchAsync(); }
    catch { preflightBranch = "unknown"; }
    preflightProgress.PrintHeader(preflightRunId, "Preflight", preflightBranch);
    preflightProgress.StartStage("Preflight", "Preflight checks still running");
    var checks = await preflight.RunAsync();
    var canRun = PreflightService.CanRun(checks);
    preflightProgress.CompleteStage(canRun ? "Preflight passed" : "Preflight failed", canRun);
    foreach (var check in checks) Console.WriteLine($"{check.Name,-12} {check.Status,-12} {check.Detail}");
    if (!canRun)
    {
        var failedChecks = string.Join(", ", checks.Where(x => x.Critical && x.Status != "OK").Select(x => x.Name));
        preflightProgress.PrintFailure(preflightRunId, "Preflight", $"Critical checks failed: {failedChecks}");
    }
    return canRun ? 0 : 1;
}

var commandStore = new RunStore(repository, options.Orchestrator.RunsDirectory);
var roadmapPath = Path.Combine(repository, "ai", "roadmap", "MILESTONES.json");
if (args[0] == "qa")
{
    if (args.Length < 2 || args[1].Equals("help", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("Usage: qa fast | qa milestone <ID> | qa full | qa report <RUN_ID>");
        return 0;
    }
    if (args[1].Equals("report", StringComparison.OrdinalIgnoreCase))
    {
        if (args.Length < 3) { Console.Error.WriteLine("Usage: qa report <RUN_ID>"); return 2; }
        var report = Path.Combine(repository, options.Orchestrator.RunsDirectory, Path.GetFileName(args[2]), "qa", "report.html");
        Console.WriteLine(File.Exists(report) ? report : $"Report not found: {report}");
        return File.Exists(report) ? 0 : 1;
    }
    var profile = args[1].Equals("fast", StringComparison.OrdinalIgnoreCase) ? E2EProfile.Fast
        : args[1].Equals("milestone", StringComparison.OrdinalIgnoreCase) ? E2EProfile.Milestone
        : args[1].Equals("full", StringComparison.OrdinalIgnoreCase) ? E2EProfile.Full
        : throw new InvalidOperationException("Unknown QA profile. Use fast, milestone, or full.");
    var qaRunId = $"QA-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..24];
    var qaRequest = new E2ETestRequest(qaRunId, null, args.Length > 2 && profile == E2EProfile.Milestone ? args[2] : null,
        profile, Device: options.Qa.Device, CaptureScreenshots: options.Qa.CaptureScreenshots,
        RecordVideo: options.Qa.EnableVideoCapture && profile != E2EProfile.Fast);
    var qaRunner = resolvedComposition.ResolveCapability<IE2ETestRunner>("qa");
    E2ETestResult qaResult;
    if (profile == E2EProfile.Milestone)
    {
        var targets = PatrolTestSelector.Select(profile, milestoneId: qaRequest.MilestoneId);
        var runs = new List<E2ETestResult>();
        for (var index = 0; index < targets.Count; index++)
        {
            var targetRequest = qaRequest with
            {
                Target = targets[index],
                ArtifactDirectory = Path.Combine(repository, options.Orchestrator.RunsDirectory, qaRunId, "qa", "tests", $"{index + 1:00}")
            };
            Console.WriteLine($"MILESTONE test {index + 1}/{targets.Count}: {targets[index]}");
            runs.Add(await RunQaWithRetriesAsync(qaRunner, targetRequest, options.Qa));
        }
        qaResult = await AggregateQaResultsAsync(qaRequest, runs, repository, options.Orchestrator.RunsDirectory);
    }
    else
    {
        qaResult = await RunQaWithRetriesAsync(qaRunner, qaRequest, options.Qa);
    }
    Console.WriteLine($"Patrol: {profile.ToString().ToUpperInvariant()} QA | Device: {qaResult.Request.Device} | Tests: {qaResult.SelectedTests.Length} | Screenshots: {(qaRequest.CaptureScreenshots ? "enabled" : "disabled")} | Video: {(qaRequest.RecordVideo ? "enabled" : "disabled")}");
    Console.WriteLine($"Result: {(qaResult.Success ? "PASS" : "FAIL")} ({qaResult.FailureCategory})\nReport: {qaResult.ReportPath}\nArtifacts: {Path.GetDirectoryName(qaResult.ResultPath)}");
    return qaResult.Success ? 0 : 1;
}
if (args[0] == "milestone")
{
    if (args.Length < 2) { Console.Error.WriteLine("Usage: milestone <ID> | milestone approve <ID>"); return 2; }
    var roadmap = RoadmapCatalog.Load(roadmapPath);
    var stateStore = new RoadmapStateStore(repository);
    var definition = roadmap.Get(args[1].Equals("approve", StringComparison.OrdinalIgnoreCase) || args[1].Equals("finalize", StringComparison.OrdinalIgnoreCase) ? args.ElementAtOrDefault(2) ?? "" : args[1]);
    var gitDefinition = new OnlineOs.AiOrchestrator.Models.MilestoneDefinition(
        definition.Id,
        definition.Title,
        definition.Branch ?? $"feature/{definition.Id.ToLowerInvariant()}",
        options.Project.IsOnlineOsCompatibilityProfile ? "developer" : "main");
    if (args[1].Equals("finalize", StringComparison.OrdinalIgnoreCase))
    {
        var runtime = await stateStore.LoadAsync();
        var allTasksDone = runtime is not null && runtime.Tasks.Values.All(x => x.Status == MilestoneTaskStatus.Done);
        var recoveringGitFinalization = runtime?.Status == MilestoneRuntimeStatus.HumanRequired
            && allTasksDone
            && (await gitWorkflow.LoadAsync(definition.Id))?.GitLifecycleState == GitLifecycleState.HumanRequired;
        var finalization = await gitWorkflow.FinalizeMilestoneAsync(
            gitDefinition,
            runtime?.Status == MilestoneRuntimeStatus.Approved || recoveringGitFinalization);
        Console.WriteLine($"Git lifecycle: {finalization.State}\n{finalization.Summary}");
        if (finalization.Succeeded && recoveringGitFinalization && runtime is not null)
        {
            runtime.Status = MilestoneRuntimeStatus.CompleteAwaitingApproval;
            runtime.RequiresHumanCheckpoint = true;
            runtime.FailureReason = null;
            runtime.RequiredAction = null;
            runtime.CurrentTaskId = null;
            runtime.ActiveRunId = null;
            runtime.CompletedAt ??= DateTimeOffset.UtcNow;
            await stateStore.SaveAsync(runtime);
            Console.WriteLine($"MILESTONE COMPLETE: {definition.Id} ({runtime.Tasks.Count}/{runtime.Tasks.Count} DONE). Human checkpoint required.");
        }
        return finalization.Succeeded ? 0 : 1;
    }
    if (!args[1].Equals("approve", StringComparison.OrdinalIgnoreCase))
    {
        var prepared = await gitWorkflow.PrepareMilestoneAsync(gitDefinition);
        if (!prepared.Succeeded) { Console.Error.WriteLine($"Git lifecycle: {prepared.State}\n{prepared.Summary}"); return 1; }
    }
    using IProgressReporter milestoneProgress = new ConsoleProgressReporter(Console.Out);
    var milestoneOrchestrator = new Orchestrator(
        router, implementationAgent, validationRunner, reviewAgent, git, commandStore,
        new ReviewPolicy(options.ReviewPolicy), new WorkflowStateMachine(), options,
        milestoneProgress, router as IFailureDiagnoser, referenceInspector, gitDefinition.Branch);
    var milestoneRunner = new MilestoneRunner(roadmap, stateStore, commandStore, milestoneOrchestrator, gitWorkflow, Console.Out);
    try
    {
        if (args[1].Equals("approve", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 3) { Console.Error.WriteLine("Usage: milestone approve <ID>"); return 2; }
            await milestoneRunner.ApproveAsync(args[2]);
            Console.WriteLine($"Milestone {args[2]} checkpoint approved.");
            return 0;
        }
        if (string.Equals(definition.Status, "completed", StringComparison.OrdinalIgnoreCase)) { Console.WriteLine($"{definition.Id} already COMPLETE."); return 0; }
        if (definition.Tasks.Count == 0) { Console.Error.WriteLine($"Milestone {definition.Id} is a placeholder and is not executable."); return 2; }
        milestoneProgress.StartStage("Preflight", "Preflight checks still running");
        var checks = await preflight.RunAsync();
        if (!PreflightService.CanRun(checks)) { milestoneProgress.CompleteStage("Preflight failed", false); return 1; }
        milestoneProgress.CompleteStage("Preflight passed");
        var state = await milestoneRunner.RunAsync(args[1]);
        return state.Status is MilestoneRuntimeStatus.Failed or MilestoneRuntimeStatus.HumanRequired ? 1 : 0;
    }
    catch (Exception exception) { Console.Error.WriteLine(exception.Message); return 1; }
}
if (args[0] == "status")
{
    var health = await commandStore.AssessActiveAsync();
    var active = health.Health is RunHealth.Terminal or RunHealth.MissingRun or RunHealth.MalformedPointer ? null : await commandStore.FindActiveAsync();
    Console.WriteLine("----------------------------------------\nONLINEOS ORCHESTRATOR STATUS\n----------------------------------------");
    var roadmapState = await new RoadmapStateStore(repository).LoadAsync();
    if (roadmapState is not null)
    {
        var catalog = RoadmapCatalog.Load(roadmapPath);
        var milestone = catalog.Get(roadmapState.MilestoneId);
        var done = roadmapState.Tasks.Values.Count(x => x.Status == MilestoneTaskStatus.Done);
        Console.WriteLine($"Milestone: {milestone.Id} — {milestone.Title}\nProgress: {done} / {roadmapState.Tasks.Count} tasks complete\nMilestone State: {roadmapState.Status}\nHuman Checkpoint: {(roadmapState.RequiresHumanCheckpoint ? "REQUIRED" : "NO")}");
        if (roadmapState.CurrentTaskId is not null)
        {
            var current = milestone.Tasks.First(x => x.Id == roadmapState.CurrentTaskId);
            Console.WriteLine($"Current Task: {current.Id} — {current.Title}\nTask State: {roadmapState.Tasks[current.Id].Status}\nRun: {roadmapState.ActiveRunId ?? "-"}");
        }
        var next = milestone.Tasks.FirstOrDefault(x => roadmapState.Tasks[x.Id].Status is MilestoneTaskStatus.Pending or MilestoneTaskStatus.Ready && x.DependsOn.All(d => roadmapState.Tasks[d].Status == MilestoneTaskStatus.Done));
        Console.WriteLine($"Next Task: {(next is null ? "-" : $"{next.Id} — {next.Title}")}");
    }
    if (active is null) { Console.WriteLine(health.Health is RunHealth.Orphaned or RunHealth.MissingRun or RunHealth.MalformedPointer ? $"Run Health: {health.Health.ToString().ToUpperInvariant()}\nReason: {health.Reason}" : "No resumable run exists."); return 0; }
    var failure = active.LastFailure;
    var nextAction = RunHealthEvaluator.Assess(active).NextAction;
    Console.WriteLine($"Run: {active.RunId}\nTask: {active.Task.Title}\nState: {active.State}\nCurrent Stage: {active.CurrentStage ?? active.State}\nLast Failure: {failure?.Category.ToString() ?? "NONE"}");
    Console.WriteLine($"Run Health: {health.Health.ToString().ToUpperInvariant()}\n{(health.Health == RunHealth.Orphaned ? $"Reason: {health.Reason}\n" : "")}Provider: {failure?.Provider ?? "NONE"}\nRecovery: {active.RetryCounts.Values.Sum()} attempt(s)\nNext Action: {nextAction ?? "None"}\nRetry At: {active.ResumeAfter?.ToLocalTime().ToString("T") ?? "-"}\nHuman Required: {(failure?.HumanRequired == true || active.State == WorkflowState.HumanRequired ? "YES" : "NO")}");
    var latest = active.LatestValidationResults.Count > 0 ? active.LatestValidationResults : active.ValidationResults;
    Console.WriteLine($"Validation: {(latest.Count == 0 ? "NOT RUN" : latest.Any(x => x.Required && !x.Passed) ? "FAIL" : "PASS")}\nReview: {(active.Reviews.Count == 0 ? "NOT RUN" : active.Reviews[^1].Decision.ToString().ToUpperInvariant())}\n----------------------------------------");
    return 0;
}

if (args[0] == "run" && args.Length >= 2 && args[1].Equals("abandon", StringComparison.OrdinalIgnoreCase))
{
    var health = await commandStore.AssessActiveAsync();
    var active = await commandStore.FindActiveAsync();
    var reasonIndex = Array.FindIndex(args, x => x.Equals("--reason", StringComparison.OrdinalIgnoreCase));
    var reason = reasonIndex >= 0 && reasonIndex + 1 < args.Length ? args[reasonIndex + 1] : "Operator-abandoned orphaned run after deterministic health inspection.";
    if (active is null) { Console.WriteLine($"No active run exists; {health.Reason}"); return 0; }
    try
    {
        await commandStore.AbandonAsync(active.RunId, reason);
        Console.WriteLine($"Run {active.RunId} abandoned safely. Run artifacts and audit history were preserved.");
        return 0;
    }
    catch (InvalidOperationException exception) { Console.Error.WriteLine(exception.Message); return 1; }
}

if (args[0] == "audit")
{
    await BackendIntegrationAudit.AggregateAsync(repository);
    Console.WriteLine("Backend integration audit aggregated into .ai-state/backend-integration-audit.json.");
    return 0;
}

if (args[0] == "run" && args.Length >= 2 && args[1].Equals("retry", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: run retry <RUN_ID>"); return 2; }
    var run = await commandStore.LoadAsync(args[2]);
    if (run is null) { Console.Error.WriteLine($"Run '{args[2]}' does not exist."); return 1; }
    using IProgressReporter retryProgress = new ConsoleProgressReporter(Console.Out);
    var retryBranch = await git.GetBranchAsync();
    retryProgress.PrintHeader(run.RunId, run.Task.Title, retryBranch);
    var retryOrchestrator = new Orchestrator(router, implementationAgent, validationRunner,
        reviewAgent, git, commandStore, new ReviewPolicy(options.ReviewPolicy),
        new WorkflowStateMachine(), options, retryProgress, router as IFailureDiagnoser, referenceInspector, run.Git?.Branch);
    try
    {
        var retried = await retryOrchestrator.RetryHumanRequiredAsync(run);
        retryProgress.PrintFinal(retried);
        return retried.State is WorkflowState.Failed or WorkflowState.HumanRequired ? 1 : 0;
    }
    catch (InvalidOperationException exception) { Console.Error.WriteLine(exception.Message); return 1; }
}

if (args[0] == "continue")
{
    var pointerHealth = await commandStore.AssessActiveAsync();
    var active = await commandStore.FindActiveAsync();
    if (active is null)
    {
        var resumableMilestone = await new RoadmapStateStore(repository).LoadAsync();
        if (resumableMilestone?.ActiveRunId is not null)
            active = await commandStore.LoadAsync(resumableMilestone.ActiveRunId);
    }
    if (active is null) { Console.WriteLine("No resumable run exists."); return 0; }
    var activeHealth = await commandStore.AssessActiveAsync();
    if (!activeHealth.CanResume && active.State != WorkflowState.HumanRequired)
    {
        Console.Error.WriteLine($"Run {active.RunId} cannot be continued: {activeHealth.Health}. {activeHealth.Reason}");
        return 1;
    }
    if (active.State is WorkflowState.Approved or WorkflowState.Failed or WorkflowState.Completed or WorkflowState.Abandoned)
    {
        Console.WriteLine($"Run {active.RunId} is already {active.State}; it was not restarted.");
        return 0;
    }
    using IProgressReporter resumeProgress = new ConsoleProgressReporter(Console.Out);
    var resumeBranch = await git.GetBranchAsync();
    resumeProgress.PrintHeader(active.RunId, active.Task.Title, resumeBranch);
    resumeProgress.StartStage($"Resuming {active.ResumeStage ?? active.CurrentStage ?? active.State}");
    var resumeOrchestrator = new Orchestrator(
        router,
        implementationAgent,
        validationRunner,
        reviewAgent,
        git,
        commandStore,
        new ReviewPolicy(options.ReviewPolicy),
        new WorkflowStateMachine(),
        options,
        resumeProgress,
        router as IFailureDiagnoser,
        referenceInspector);
    RunRecord resumed;
    if (active.MilestoneId is not null)
    {
        var milestoneRunner = new MilestoneRunner(RoadmapCatalog.Load(roadmapPath), new RoadmapStateStore(repository), commandStore, resumeOrchestrator, gitWorkflow, Console.Out);
        await milestoneRunner.ContinueAsync(active);
        resumed = active;
    }
    else resumed = await resumeOrchestrator.ContinueAsync(active);
    resumeProgress.PrintFinal(resumed);
    return resumed.State is WorkflowState.Failed or WorkflowState.HumanRequired ? 1 : 0;
}

DevelopmentTask task;
var dryRun = args.Contains("--dry-run", StringComparer.Ordinal);
if (args[0] == "task" && args.Length >= 2)
{
    var text = args[1];
    task = new DevelopmentTask($"TASK-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}", text, text);
}
else if (args[0] == "task-file" && args.Length >= 2)
{
    task = JsonSerializer.Deserialize<DevelopmentTask>(await File.ReadAllTextAsync(args[1]), jsonOptions)
        ?? throw new InvalidOperationException("Task file did not contain a valid task.");
}
else
{
    Console.Error.WriteLine("Invalid command.");
    PrintUsage();
    return 2;
}

var runId = Orchestrator.CreateRunId();
using IProgressReporter progress = new ConsoleProgressReporter(Console.Out);
string branch;
try { branch = await git.GetBranchAsync(); }
catch { branch = "unknown"; }
progress.PrintHeader(runId, task.Title, branch);

if (!dryRun)
{
    progress.StartStage("Preflight", "Preflight checks still running");
    var checks = await preflight.RunAsync();
    if (!PreflightService.CanRun(checks))
    {
        var failedChecks = string.Join(", ", checks.Where(x => x.Critical && x.Status != "OK").Select(x => x.Name));
        var reason = $"Critical checks failed: {failedChecks}";
        progress.CompleteStage("Preflight failed", false);
        progress.PrintFailure(runId, "Preflight", reason);
        Console.Error.WriteLine("Critical preflight checks failed. No agent was started. Run 'preflight' for details.");
        return 1;
    }
    progress.CompleteStage("Preflight passed");
}

var orchestrator = new Orchestrator(
    router,
    implementationAgent,
    validationRunner,
    reviewAgent,
    git,
    new RunStore(repository, options.Orchestrator.RunsDirectory),
    new ReviewPolicy(options.ReviewPolicy),
    new WorkflowStateMachine(),
    options,
    progress,
    router as IFailureDiagnoser,
    referenceInspector);

var result = await orchestrator.ExecuteAsync(task, dryRun, runId: runId, headerAlreadyPrinted: true);
if (dryRun && result.Plan is not null)
    Console.WriteLine($"Dry run routed: {result.Plan.Routing.TaskType} | risk={result.Plan.Routing.Risk} | pipeline={result.Plan.Routing.RecommendedPipeline}");
else
    progress.PrintFinal(result.Run);
return result.Run.State == WorkflowState.Failed ? 1 : 0;

static string? FindRepository(string start)
{
    var directory = new DirectoryInfo(Path.GetFullPath(start));
    while (directory is not null)
    {
        if (Directory.Exists(Path.Combine(directory.FullName, ".git"))) return directory.FullName;
        directory = directory.Parent;
    }
    return null;
}

static string FindConfigDirectory(string current, string binary)
{
    var candidates = new[]
    {
        Path.Combine(current, "tools", "ai-orchestrator"), current
    };
    return candidates.FirstOrDefault(path => File.Exists(Path.Combine(path, "appsettings.json"))) ?? Path.GetFullPath(current);
}

static void PrintUsage()
{
    Console.WriteLine("OnlineOS AI Orchestrator");
    Console.WriteLine("  dotnet run -- preflight");
    Console.WriteLine("  dotnet run -- task \"Implement ...\" [--dry-run]");
    Console.WriteLine("  dotnet run -- task-file task.json [--dry-run]");
    Console.WriteLine("  dotnet run -- milestone M2");
    Console.WriteLine("  dotnet run -- milestone approve M2");
    Console.WriteLine("  dotnet run -- continue");
    Console.WriteLine("  dotnet run -- run abandon [--reason \"...\"]");
    Console.WriteLine("  dotnet run -- run retry <RUN_ID>");
    Console.WriteLine("  dotnet run -- audit");
    Console.WriteLine("  dotnet run -- qa fast");
    Console.WriteLine("  dotnet run -- qa milestone <ID>");
    Console.WriteLine("  dotnet run -- qa full");
    Console.WriteLine("  dotnet run -- qa report <RUN_ID>");
}

static async Task<E2ETestResult> RunQaWithRetriesAsync(IE2ETestRunner runner, E2ETestRequest request, QaOptions options)
{
    var result = await runner.RunAsync(request);
    var attempts = 0;
    while (!result.Success && IsRetryableQaFailure(result.FailureCategory) && attempts < options.MaxInfrastructureRetries)
    {
        attempts++;
        Console.WriteLine($"Patrol infrastructure retry {attempts}/{options.MaxInfrastructureRetries}: {result.FailureCategory}");
        result = await runner.RunAsync(request);
    }
    return result;
}

static async Task<E2ETestResult> AggregateQaResultsAsync(E2ETestRequest request, IReadOnlyList<E2ETestResult> runs, string repository, string runsDirectory)
{
    var directory = Path.Combine(repository, runsDirectory, request.RunId, "qa");
    Directory.CreateDirectory(Path.Combine(directory, "logs"));
    var screenshotDirectory = Path.Combine(directory, "screenshots");
    Directory.CreateDirectory(screenshotDirectory);
    var stdoutPath = Path.Combine(directory, "logs", "stdout.log");
    var stderrPath = Path.Combine(directory, "logs", "stderr.log");
    await File.WriteAllTextAsync(stdoutPath, string.Join(Environment.NewLine, runs.Select(run => File.Exists(run.StdoutPath) ? File.ReadAllText(run.StdoutPath) : "")));
    await File.WriteAllTextAsync(stderrPath, string.Join(Environment.NewLine, runs.Select(run => File.Exists(run.StderrPath) ? File.ReadAllText(run.StderrPath) : "")));
    var screenshots = new List<string>();
    for (var index = 0; index < runs.Count; index++)
    {
        foreach (var source in runs[index].Screenshots.Where(File.Exists))
        {
            var destination = Path.Combine(screenshotDirectory, $"{index + 1:00}-{Path.GetFileName(source)}");
            File.Copy(source, destination, overwrite: true);
            screenshots.Add(destination);
        }
    }
    var aggregate = new E2ETestResult(
        runs.All(run => run.Success),
        runs.LastOrDefault()?.ExitCode ?? -1,
        TimeSpan.FromTicks(runs.Sum(run => run.Duration.Ticks)),
        request with { Target = null, ArtifactDirectory = directory },
        runs.SelectMany(run => run.SelectedTests).Distinct().ToArray(),
        screenshots.Distinct().ToArray(),
        runs.SelectMany(run => run.Videos).Distinct().ToArray(),
        stdoutPath,
        stderrPath,
        Path.Combine(directory, "result.json"),
        "",
        runs.FirstOrDefault(run => !run.Success)?.FailureCategory ?? E2EFailureCategory.None,
        string.Join(Environment.NewLine, runs.Where(run => !run.Success).Select(run => run.FailureDetail)),
        runs.SelectMany(run => run.MissingScreenshots ?? []).ToArray(),
        runs.Select(run => run.PatrolExecutable).FirstOrDefault(value => value is not null),
        runs.Select(run => run.PatrolWorkingDirectory).FirstOrDefault(value => value is not null));
    var report = await QaReportWriter.WriteAsync(directory, aggregate);
    aggregate = aggregate with { ReportPath = report };
    await File.WriteAllTextAsync(aggregate.ResultPath, JsonSerializer.Serialize(aggregate, new JsonSerializerOptions { WriteIndented = true }));
    return aggregate;
}

static bool IsRetryableQaFailure(E2EFailureCategory category) => category is E2EFailureCategory.EmulatorUnavailable
    or E2EFailureCategory.EmulatorStartupFailure or E2EFailureCategory.EmulatorBootTimeout
    or E2EFailureCategory.DeviceDisconnected or E2EFailureCategory.AdbFailure or E2EFailureCategory.PatrolInfrastructureFailure;
