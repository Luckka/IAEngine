using OnlineOs.AiOrchestrator.Hosting;

namespace IAEngine.Core.Tests;

public sealed class M11BIndependenceTests
{
    [Fact]
    public void CoreProjectUsesItsOwnRootNamespaceAndDoesNotCompileTheLegacyGitWorkflowManager()
    {
        var root = RepositoryRoot();
        var project = File.ReadAllText(Path.Combine(root, "src", "IAEngine.Core", "IAEngine.Core.csproj"));

        Assert.Contains("<RootNamespace>IAEngine.Core</RootNamespace>", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Pipeline/GitWorkflowManager.cs", project, StringComparison.Ordinal);
        Assert.DoesNotContain("IAEngine.OnlineOSAdapter", project, StringComparison.Ordinal);
    }

    [Fact]
    public void CoreAssemblyDoesNotReferenceTheOnlineOsAdapter()
    {
        var references = typeof(EngineHost).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? "")
            .ToArray();

        Assert.DoesNotContain(references, name => string.Equals(name, "IAEngine.OnlineOSAdapter", StringComparison.Ordinal));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        return directory?.FullName ?? Directory.GetCurrentDirectory();
    }
}
