using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Infrastructure;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Pipeline;

namespace OnlineOs.AiOrchestrator.Tests;

public sealed class GitWorkflowManagerTests
{
    [Fact]
    public async Task PrepareCreatesFeatureFromLatestDeveloperAndPersistsBase()
    {
        await using var repo = await TempRepository.CreateAsync();
        var manager = repo.Manager();

        var result = await manager.PrepareMilestoneAsync(repo.Milestone("M2", "feature/m2-technician-vertical-slice"));

        Assert.True(result.Succeeded);
        Assert.Equal(GitLifecycleState.BranchReady, result.State);
        Assert.Equal("feature/m2-technician-vertical-slice", await repo.Git("branch", "--show-current"));
        Assert.Equal(await repo.Git("rev-parse", "developer"), result.Metadata.BaseCommit);
        Assert.Equal(result.Metadata, await manager.LoadAsync("M2"));
    }

    [Fact]
    public async Task DirtyTreeAndAheadDeveloperRequireHuman()
    {
        await using var repo = await TempRepository.CreateAsync();
        await repo.WriteAsync("unowned.txt", "do not touch");
        var dirty = await repo.Manager().PrepareMilestoneAsync(repo.Milestone("M2", "feature/m2"));
        Assert.Equal(GitLifecycleState.HumanRequired, dirty.State);

        await repo.WriteAsync("local.txt", "local developer work");
        await repo.CommitAsync("local developer work");
        var ahead = await repo.Manager().PrepareMilestoneAsync(repo.Milestone("M2", "feature/m2"));
        Assert.Equal(GitLifecycleState.HumanRequired, ahead.State);
        Assert.Contains("ahead", ahead.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FinalizePushesFeatureMergesAndValidatesDeveloper()
    {
        await using var repo = await TempRepository.CreateAsync();
        var manager = repo.Manager(new PassingValidation());
        var milestone = repo.Milestone("M2", "feature/m2");
        Assert.True((await manager.PrepareMilestoneAsync(milestone)).Succeeded);
        await repo.WriteAsync("app/feature.txt", "milestone");

        var result = await manager.FinalizeMilestoneAsync(milestone, gatesPassed: true);

        Assert.True(result.Succeeded, result.Summary);
        Assert.Equal(GitLifecycleState.Published, result.State);
        Assert.Equal(await repo.Git("rev-parse", "developer"), await repo.Git("rev-parse", "origin/developer"));
        Assert.Equal(await repo.Git("rev-parse", "feature/m2"), await repo.Git("rev-parse", "origin/feature/m2"));
        Assert.True(result.Metadata.PostMergeValidation);
    }

    [Fact]
    public async Task ProhibitedGitOperationsAreRejectedByTheManager()
    {
        await using var repo = await TempRepository.CreateAsync();
        var manager = repo.Manager();
        var result = await manager.PrepareMilestoneAsync(repo.Milestone("M2", "feature/m2", "producao"));
        Assert.Equal(GitLifecycleState.HumanRequired, result.State);
        Assert.DoesNotContain(repo.Commands, command => command.Contains("reset --hard", StringComparison.Ordinal));
        Assert.DoesNotContain(repo.Commands, command => command.Contains("rebase", StringComparison.Ordinal));
        Assert.DoesNotContain(repo.Commands, command => command.Contains("clean", StringComparison.Ordinal));
        Assert.DoesNotContain(repo.Commands, command => command.Contains("stash", StringComparison.Ordinal));
        Assert.DoesNotContain(repo.Commands, command => command.Contains("--force", StringComparison.Ordinal));
    }

    private sealed class PassingValidation : IValidationRunner
    {
        public Task<IReadOnlyList<ValidationResult>> RunAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ValidationResult>>([TestData.Validation(true)]);
    }

    private sealed class TempRepository : IAsyncDisposable
    {
        private readonly string path;
        public List<string> Commands { get; } = [];
        private TempRepository(string path) => this.path = path;
        public static async Task<TempRepository> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "onlineos-git-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var repo = new TempRepository(root);
            await repo.Git("init", "--initial-branch=developer");
            await repo.Git("config", "user.email", "tests@example.invalid");
            await repo.Git("config", "user.name", "Tests");
            await repo.WriteAsync("README.md", "base");
            foreach (var directory in new[] { "app", "tools/ai-orchestrator", "ai", "architecture", "docs" }) Directory.CreateDirectory(Path.Combine(root, directory));
            await repo.CommitAsync("base");
            var bare = root + "-origin.git";
            Directory.CreateDirectory(bare);
            await RunGit(bare, "init", "--bare");
            await repo.Git("remote", "add", "origin", bare);
            await repo.Git("push", "-u", "origin", "developer");
            return repo;
        }
        public MilestoneDefinition Milestone(string id, string branch, string baseBranch = "developer") => new(id, id, branch, baseBranch);
        public GitWorkflowManager Manager(IValidationRunner? validation = null) => new(new RecordingRunner(this), path, new GitOptions(), validation);
        public async Task<string> Git(params string[] args) { var result = await RunGit(path, args); Commands.Add(result.Command); return result.StandardOutput.Trim(); }
        public Task WriteAsync(string relative, string content) { var file = Path.Combine(path, relative); Directory.CreateDirectory(Path.GetDirectoryName(file)!); return File.WriteAllTextAsync(file, content); }
        public Task CommitAsync(string message) => Git("add", "--all").ContinueWith(_ => Git("commit", "-m", message)).Unwrap();
        public async Task CleanUntrackedAsync() { foreach (var file in Directory.GetFiles(path, "unowned.txt")) File.Delete(file); }
        public async ValueTask DisposeAsync() { try { Directory.Delete(path, true); Directory.Delete(path + "-origin.git", true); } catch { } await Task.CompletedTask; }
        private static async Task<ProcessResult> RunGit(string cwd, params string[] args) => await new ProcessRunner().RunAsync(new ProcessSpec("git", args, cwd));
        private sealed class RecordingRunner(TempRepository owner) : IProcessRunner { public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default) { owner.Commands.Add(string.Join(' ', new[] { spec.FileName }.Concat(spec.Arguments))); return new ProcessRunner().RunAsync(spec, ct); } }
    }
}
