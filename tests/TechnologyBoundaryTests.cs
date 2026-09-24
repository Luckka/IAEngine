using OnlineOs.AiOrchestrator.Hosting;
using OnlineOs.AiOrchestrator.Roadmap;

namespace OnlineOs.AiOrchestrator.Tests;

public sealed class TechnologyBoundaryTests
{
    [Fact]
    public void EngineHostAssemblyHasNoDirectTechnologyProviderAssemblyReferences()
    {
        var referencedAssemblies = typeof(EngineHost).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? "")
            .ToArray();

        Assert.DoesNotContain(referencedAssemblies, name => name.Contains("Flutter", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencedAssemblies, name => name.Contains("Patrol", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencedAssemblies, name => name.Contains("Amazon", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencedAssemblies, name => name.Contains("Aws", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GenericRoadmapValidationRejectsMissingDependenciesBeforeExecution()
    {
        var definition = new MilestoneDefinition
        {
            Id = "generic-boundary",
            Title = "Generic boundary",
            Tasks = [new() { Id = "task-1", Title = "Task", DependsOn = ["missing-task"] }]
        };

        var exception = Assert.Throws<InvalidOperationException>(() => RoadmapCatalog.FromMilestone(definition));

        Assert.Contains("depends on missing task", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
