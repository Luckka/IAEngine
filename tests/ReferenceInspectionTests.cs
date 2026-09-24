using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Infrastructure;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Reference;

namespace OnlineOs.AiOrchestrator.Tests;

public sealed class ReferenceInspectionTests
{
    [Fact]
    public void ReadOnlyPolicyAllowsInspectionAndRejectsMutation()
    {
        Assert.True(ReadOnlyReferenceBoundary.IsSafeReadCommand("git -C reference branch --show-current"));
        Assert.True(ReadOnlyReferenceBoundary.IsSafeReadCommand("rg Controller"));
        Assert.False(ReadOnlyReferenceBoundary.IsSafeReadCommand("git -C reference checkout main"));
        Assert.False(ReadOnlyReferenceBoundary.IsSafeReadCommand("git -C reference status; touch x"));
        Assert.False(ReadOnlyReferenceBoundary.IsSafeReadCommand("rm -rf reference"));
    }

    [Fact]
    public void ReferenceBoundaryRejectsTraversalAndSymlinkEscape()
    {
        var root = NewDirectory();
        var outside = NewDirectory();
        var link = Path.Combine(root, "escape");
        Directory.CreateSymbolicLink(link, outside);
        var boundary = new ReadOnlyReferenceBoundary(root);
        Assert.False(boundary.Contains(Path.Combine(root, "..", Path.GetFileName(outside), "secret")));
        Assert.False(boundary.Contains(Path.Combine(link, "secret")));
    }

    [Fact]
    public void BoundedDiscoveryClassifiesMonolithAreas()
    {
        var root = NewDirectory();
        Directory.CreateDirectory(Path.Combine(root, "Api"));
        Directory.CreateDirectory(Path.Combine(root, "Infrastructure"));
        Directory.CreateDirectory(Path.Combine(root, "legacy-frontend"));
        File.WriteAllText(Path.Combine(root, "ServiceOrderController.cs"), "");
        var index = MonolithReferenceInspector.Discover(new ReadOnlyReferenceBoundary(root), "b1208", "abc", true);
        Assert.Contains("Api", index.BackendApplicationRoots);
        Assert.Contains("Infrastructure", index.InfrastructureRoots);
        Assert.Contains("legacy-frontend", index.LegacyFrontendRoots);
        Assert.True(index.WorkingTreeDirty);
        Assert.Equal("abc", index.SourceRevision);
    }

    [Fact]
    public async Task WrongBranchIsReportedWithoutSwitching()
    {
        var writable = NewDirectory();
        var reference = NewDirectory();
        var inspector = new MonolithReferenceInspector(new FakeReferenceGit("feature/wrong"), writable,
            new ReferenceOptions { Root = reference, RequiredBranch = "b1208", Enabled = true });
        var result = await inspector.InspectAsync(new DevelopmentTask("M2-001", "Agenda", "agenda"));
        Assert.Equal("REFERENCE_BRANCH_MISMATCH", result.ErrorCode);
        Assert.False(result.ReferenceAvailable);
    }

    [Fact]
    public void SummaryIsConciseAndKeepsFrontendIndependent()
    {
        var artifact = new BackendReferenceArtifact
        {
            ReferenceStatus = "INSPECTED", ReferenceBranch = "b1208", ReferenceCommit = "abc",
            ConfirmedContracts = [new ReferenceFinding("ServiceOrder.Id", "Api/Dto.cs", "Guid", CompatibilityClassification.CONFIRMED)],
            DeferredItems = [new BackendGap("GAP", "Mobile sync is not confirmed", "local mock", "M6")]
        };
        var summary = OnlineOs.AiOrchestrator.Reference.OnlineOsReferenceContextProvider.BuildSummary(artifact);
        Assert.Contains("ServiceOrder.Id", summary);
        Assert.Contains("no real HTTP integration", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("legacy architecture", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingReferenceIsNonBlocking()
    {
        var inspector = new MonolithReferenceInspector(new FakeReferenceGit("b1208"), NewDirectory(), new ReferenceOptions { Root = Path.Combine(Path.GetTempPath(), "does-not-exist-onlineos") });
        var result = await inspector.InspectAsync(new DevelopmentTask("M3-001", "Offline", "offline"));
        Assert.Equal("REFERENCE_UNAVAILABLE", result.ErrorCode);
        Assert.False(result.HumanRequired);
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "onlineos-reference-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeReferenceGit(string branch) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken = default)
        {
            var output = spec.Arguments.Contains("--show-current") ? branch : spec.Arguments.Contains("HEAD") ? "abc" : "";
            return Task.FromResult(new ProcessResult("git", 0, output, "", TimeSpan.Zero));
        }
    }
}
