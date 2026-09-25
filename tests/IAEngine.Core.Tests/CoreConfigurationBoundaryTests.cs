using OnlineOs.AiOrchestrator.Configuration;

namespace IAEngine.Core.Tests;

public sealed class CoreConfigurationBoundaryTests
{
    [Fact]
    public void RepositoryDefaultConfigurationIsGenericAndDoesNotRegisterOnlineOsComponents()
    {
        var root = RepositoryRoot();
        var options = ConfigLoader.LoadFile(Path.Combine(root, "appsettings.json"));

        Assert.True(options.ProfileDeclared);
        Assert.Equal("iaengine-generic", options.Project.Id);
        Assert.Equal("generic", options.Project.Stack);
        Assert.False(options.Project.IsOnlineOsCompatibilityProfile);
        Assert.Empty(options.Project.Composition.Providers);
        Assert.Empty(options.Project.Composition.Capabilities);
        Assert.Empty(ProjectProfileValidator.ValidateAppOptions(options));
    }

    [Fact]
    public void OnlineOsCompatibilityConfigurationRequiresExplicitSelection()
    {
        var root = RepositoryRoot();
        var options = ConfigLoader.LoadFile(Path.Combine(root, "appsettings.onlineos.json"));

        Assert.True(options.Project.IsOnlineOsCompatibilityProfile);
        Assert.Equal("flutter", options.Project.Stack);
        Assert.Contains("ollama", options.Project.Composition.Providers);
        Assert.Contains("qa", options.Project.Composition.Capabilities);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        return directory?.FullName ?? Directory.GetCurrentDirectory();
    }
}
