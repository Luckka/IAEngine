using OnlineOs.AiOrchestrator.Infrastructure;

namespace OnlineOs.AiOrchestrator.Tests;

public sealed class WorkspaceBoundaryTests
{
    [Fact]
    public void AcceptsRepositoryLocalPath()
    {
        var root = Directory.GetCurrentDirectory();
        var boundary = new WorkspaceBoundary(root);

        Assert.True(boundary.Contains(Path.Combine(root, "app", "lib", "main.dart")));
        Assert.Equal(Path.Combine(root, "app", "lib", "main.dart"), boundary.Resolve("app/lib/main.dart"));
    }

    [Theory]
    [InlineData("../other-project")]
    [InlineData("../../secret")]
    [InlineData("/absolute/path/outside/repository")]
    public void RejectsTraversalAndAbsoluteOutsidePath(string path)
    {
        var boundary = new WorkspaceBoundary(Directory.GetCurrentDirectory());

        Assert.False(boundary.Contains(path));
        Assert.Throws<InvalidOperationException>(() => boundary.Resolve(path));
    }

    [Fact]
    public void RejectsPrefixConfusionPath()
    {
        var boundary = new WorkspaceBoundary(Directory.GetCurrentDirectory());
        var malicious = boundary.Root + "-malicious";

        Assert.False(boundary.Contains(malicious));
    }

    [Fact]
    public void RejectsSiblingRepositoryPath()
    {
        var boundary = new WorkspaceBoundary(Directory.GetCurrentDirectory());
        var sibling = Path.Combine(Directory.GetParent(boundary.Root)!.FullName, "other-project");

        Assert.False(boundary.Contains(sibling));
    }
}
