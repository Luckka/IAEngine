using System.Text.RegularExpressions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Pipeline;

namespace OnlineOs.AiOrchestrator.Tests;

public sealed class M1CoreBoundaryTests
{
    [Fact]
    public void GenericProfileHasNoOnlineOsTechnologyDefaults()
    {
        var profile = new ProjectProfileOptions();

        Assert.Equal("engine-project", profile.Id);
        Assert.Equal("unspecified", profile.Stack);
        Assert.DoesNotContain(profile.BranchPolicy.ProtectedBranches, branch => branch is "developer" or "producao");
        Assert.Empty(profile.Commands);
        Assert.Empty(profile.Validators);
        Assert.Empty(profile.Policies);
        Assert.Empty(profile.ContextPaths);
        Assert.Empty(ProjectProfileValidator.Validate(profile));
    }

    [Fact]
    public void OnlineOsValuesAreExplicitProfileDataRatherThanGenericDefaults()
    {
        var options = ConfigLoader.LoadFile(Path.Combine(RepositoryRoot(), "appsettings.onlineos.json"));

        Assert.True(options.ProfileDeclared);
        Assert.Equal("onlineos-mobile", options.Project.Id);
        Assert.Equal("flutter", options.Project.Stack);
        Assert.Contains("prototype-evidence", options.Project.Policies);
        Assert.Contains("developer", options.Project.BranchPolicy.ProtectedBranches);
        Assert.Equal("b1208", options.Reference.RequiredBranch);
        Assert.Equal("OnlineOS-QA", options.Qa.Device);
        Assert.True(ProjectProfileValidator.ValidateAppOptions(options).Count == 0);
        var composition = EngineComposition.Create(options);
        Assert.IsAssignableFrom<IProjectComposition>(composition);
        Assert.True(composition.IsOnlineOsCompatibility);
        Assert.True(composition.RegistersValidation);
        Assert.True(composition.RegistersQa);
        Assert.True(composition.RegistersReferenceInspection);
        Assert.True(composition.RegistersOnlineOsPolicies);
        Assert.Contains("ollama", composition.Providers);
        Assert.Contains("qa", composition.Capabilities);
    }

    [Fact]
    public void GenericConfigurationHasNeutralSectionsAndNoOnlineOsEnvironmentFallback()
    {
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "generic.json");
            File.WriteAllText(path, """
                {
                  "Project": {
                    "Id": "generic-consumer",
                    "WorkspaceRoot": ".",
                    "Stack": "dotnet",
                    "Commands": ["build"],
                    "Validators": ["dotnet"],
                    "Policies": ["generic-policy"],
                    "ContextPaths": ["docs/generic"],
                    "Composition": { "Providers": ["local"], "Capabilities": ["generic-validation"] }
                  }
                }
                """);

            var previousModel = Environment.GetEnvironmentVariable("ONLINEOS_OLLAMA_MODEL");
            var previousReference = Environment.GetEnvironmentVariable("ONLINEOS_REFERENCE_ROOT");
            Environment.SetEnvironmentVariable("ONLINEOS_OLLAMA_MODEL", "qwen3:4b");
            Environment.SetEnvironmentVariable("ONLINEOS_REFERENCE_ROOT", "/onlineos-reference-must-not-load");
            AppOptions options;
            try { options = ConfigLoader.LoadFile(path); }
            finally
            {
                Environment.SetEnvironmentVariable("ONLINEOS_OLLAMA_MODEL", previousModel);
                Environment.SetEnvironmentVariable("ONLINEOS_REFERENCE_ROOT", previousReference);
            }
            var errors = ProjectProfileValidator.ValidateAppOptions(options);
            var composition = EngineComposition.Create(options);

            Assert.True(options.ProfileDeclared);
            Assert.True(errors.Count == 0, string.Join(" | ", errors));
            Assert.Equal("generic-consumer", options.Project.Id);
            Assert.Equal("", options.Reference.RequiredBranch);
            Assert.False(options.Reference.Enabled);
            Assert.Equal("", options.Qa.ProjectPath);
            Assert.Equal("", options.Qa.Device);
            Assert.Equal("", options.Validation.Flutter.ProjectPath);
            Assert.Equal("", options.Validation.Flutter.DeviceProbeCommand);
            Assert.Equal(["main", "master"], options.Git.ProtectedBranches);
            Assert.DoesNotContain(options.Project.ContextPaths, path => path.Contains("OnlineOS", StringComparison.OrdinalIgnoreCase));
            Assert.False(composition.IsOnlineOsCompatibility);
            Assert.Equal("generic-consumer", ((IProjectComposition)composition).ProjectId);
            Assert.True(composition.RegistersValidation);
            Assert.False(composition.RegistersQa);
            Assert.False(composition.RegistersReferenceInspection);
            Assert.False(composition.RegistersOnlineOsPolicies);
            Assert.Equal(["local"], composition.Providers);
            Assert.Equal(["dotnet"], composition.Validators);
            Assert.Equal(["generic-policy"], composition.Policies);
            Assert.Equal(["generic-validation"], composition.Capabilities);
            Assert.Empty(composition.ContextPaths);

            var invalidPath = Path.Combine(directory, "invalid-generic.json");
            File.WriteAllText(invalidPath, "{ \"Project\": { \"Id\": \"generic-consumer\", \"Composition\": { \"Providers\": [\"ollama\"] } } }");
            var invalidGeneric = ConfigLoader.LoadFile(invalidPath);
            Assert.Contains(ProjectProfileValidator.ValidateAppOptions(invalidGeneric), error => error.Contains("OnlineOS-specific", StringComparison.Ordinal));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void MissingProfileFailsBeforeProviderComposition()
    {
        var options = ConfigLoader.LoadFile(Path.Combine(NewDirectory(), "missing.json"));

        var errors = ProjectProfileValidator.ValidateAppOptions(options);
        var composition = EngineComposition.Create(options);

        Assert.Contains(errors, error => error.Contains("Project profile is required", StringComparison.Ordinal));
        Assert.False(options.ProfileDeclared);
        Assert.False(composition.IsOnlineOsCompatibility);
        Assert.False(composition.RegistersValidation);
        Assert.False(composition.RegistersQa);
        Assert.False(composition.RegistersReferenceInspection);
        Assert.False(composition.RegistersOnlineOsPolicies);
    }

    [Fact]
    public void IncompleteOnlineOsProfileFailsWithFieldSpecificErrors()
    {
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "incomplete.json");
            File.WriteAllText(path, "{ \"Project\": { \"Id\": \"onlineos-mobile\", \"Stack\": \"flutter\" } }");

            var errors = ProjectProfileValidator.ValidateAppOptions(ConfigLoader.LoadFile(path));

            Assert.Contains(errors, error => error.Contains("Qa.ProjectPath", StringComparison.Ordinal));
            Assert.Contains(errors, error => error.Contains("Qa.Device", StringComparison.Ordinal));
            Assert.Contains(errors, error => error.Contains("Validation.Commands", StringComparison.Ordinal));
            Assert.Contains(errors, error => error.Contains("Reference.RequiredBranch", StringComparison.Ordinal));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void ConfigurationPrecedenceNeverFallsBackToBinaryDirectory()
    {
        var directory = NewDirectory();
        try
        {
            var direct = Path.Combine(directory, "appsettings.json");
            Assert.Equal(direct, ConfigLoader.ResolveConfigPath(directory));

            var overridePath = Path.Combine(directory, "explicit.json");
            var previousOverride = Environment.GetEnvironmentVariable("ONLINEOS_ORCHESTRATOR_CONFIG");
            Environment.SetEnvironmentVariable("ONLINEOS_ORCHESTRATOR_CONFIG", overridePath);
            try { Assert.Equal(overridePath, ConfigLoader.ResolveConfigPath(directory)); }
            finally { Environment.SetEnvironmentVariable("ONLINEOS_ORCHESTRATOR_CONFIG", previousOverride); }

            var legacy = Path.Combine(directory, "tools", "ai-orchestrator", "appsettings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
            File.WriteAllText(legacy, "{}");
            Assert.Equal(legacy, ConfigLoader.ResolveConfigPath(directory));

            File.WriteAllText(direct, "{}");
            Assert.Equal(direct, ConfigLoader.ResolveConfigPath(directory));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void GenericCoreFilesDoNotReferenceKnownProjectTechnologies()
    {
        var root = RepositoryRoot();
        var coreFiles = new[]
        {
            "Abstractions/Contracts.cs",
            "Abstractions/GitWorkflowContracts.cs",
            "Infrastructure/WorkspaceBoundary.cs",
            "Infrastructure/RunStore.cs",
            "Pipeline/WorkflowStateMachine.cs"
        };
        var forbidden = new[] { "OnlineOS", "Flutter", "Dart", "Patrol", "ADB", "b1208", "developer", "producao", "prototype conformity", "AWS", "CodeUp", "FinGuard" };

        foreach (var relative in coreFiles)
        {
            var source = Regex.Replace(File.ReadAllText(Path.Combine(root, relative)), "\\\"(?:\\\\.|[^\\\"\\\\])*\\\"", "\\\"\\\"");
            foreach (var token in forbidden)
                Assert.DoesNotContain(token, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void WorkflowStateMachinePreservesSuccessfulAndHumanRequiredRecoveryPaths()
    {
        var run = TestData.Run();
        var machine = new WorkflowStateMachine();
        foreach (var state in new[] { WorkflowState.Routing, WorkflowState.Routed, WorkflowState.Implementing, WorkflowState.Validating, WorkflowState.Reviewing, WorkflowState.Approved })
            machine.Move(run, state, "M1 characterization");

        Assert.Equal(WorkflowState.Approved, run.State);
        Assert.Equal(6, run.Transitions.Count);

        var human = TestData.Run();
        human.State = WorkflowState.HumanRequired;
        human.LastFailure = TestData.Failure(FailureCategory.ReviewFailure, humanRequired: true);
        human.Reviews.Add(new ReviewResult(ReviewDecision.Fail, [new ReviewFinding(FindingSeverity.Critical, "file", "line", "problem", "impact", "fix")], "blocked"));
        machine.ReopenForAuthorizedReviewRecovery(human, "authorized recovery");
        Assert.Equal(WorkflowState.Validating, human.State);
        Assert.Null(human.LastFailure);
    }

    [Fact]
    public void RetryAndRemediationLimitsRemainTheCompatibilityValues()
    {
        var options = new OrchestratorOptions();

        Assert.Equal(3, options.ProviderTransientRetries);
        Assert.Equal(2, options.MalformedOutputRetries);
        Assert.Equal(1, options.UnknownDiagnosisRetries);
        Assert.Equal(5, options.DeterministicValidationRemediationCycles);
        Assert.Equal(5, options.EngineeringRemediationCycles);
        Assert.Equal(2, options.EngineeringProgressExtensions);
        Assert.Equal(1800, options.ProcessTimeoutSeconds);
    }

    [Fact]
    public void CoreAssemblyDoesNotReferenceAnOnlineOsApplicationAssembly()
    {
        var references = typeof(WorkflowStateMachine).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, reference => reference.Name is "OnlineOS.Mobile" or "Flutter" or "Patrol");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory?.FullName ?? Directory.GetCurrentDirectory();
    }

    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "engine-m1-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
