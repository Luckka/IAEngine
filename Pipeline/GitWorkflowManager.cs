using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Infrastructure;
using OnlineOs.AiOrchestrator.Models;

namespace OnlineOs.AiOrchestrator.Pipeline;

/// <summary>Owns every milestone branch transition. Agents receive no Git decision-making authority.</summary>
public sealed class GitWorkflowManager(
    IProcessRunner processes,
    string repository,
    GitOptions options,
    IValidationRunner? validation = null,
    GitLifecycleStore? store = null) : IGitWorkflowManager
{
    private readonly GitLifecycleStore state = store ?? new GitLifecycleStore(repository);

    public Task<GitLifecycleMetadata?> LoadAsync(string milestoneId, CancellationToken cancellationToken = default) => state.LoadAsync(milestoneId, cancellationToken);

    public async Task<GitWorkflowResult> PrepareMilestoneAsync(MilestoneDefinition milestone, string? runId = null, CancellationToken ct = default)
    {
        var metadata = NewMetadata(milestone);
        try
        {
            await RequireRepositoryAsync(ct);
            await RequireCleanAndNoOperationInProgressAsync(ct);
            if (milestone.BaseBranch is "producao" or "main" or "master"
                || (milestone.BaseBranch != "developer" && options.ProtectedBranches.Contains(milestone.BaseBranch, StringComparer.OrdinalIgnoreCase)))
                return await HumanAsync(milestone.Id, metadata, "Production/protected branches are never an automatic milestone base.", ct);
            if (!milestone.Branch.StartsWith("feature/", StringComparison.Ordinal)) return await HumanAsync(milestone.Id, metadata, "Milestone branches must use the feature/ namespace.", ct);

            await RunAsync(["fetch", "origin"], ct);
            var baseSha = await RevParseAsync(milestone.BaseBranch, ct);
            var remoteBase = await RevParseAsync($"origin/{milestone.BaseBranch}", ct);
            var relation = await RelationAsync(baseSha, remoteBase, ct);
            if (relation == BranchRelation.Ahead || relation == BranchRelation.Diverged)
            {
                var reason = $"{milestone.BaseBranch} is {relation.ToString().ToLowerInvariant()} of origin/{milestone.BaseBranch}.";
                return await HumanAsync(milestone.Id, metadata with { GitFailure = reason }, reason, ct);
            }
            if (relation == BranchRelation.Behind)
            {
                await RunAsync(["switch", milestone.BaseBranch], ct);
                await RunAsync(["merge", "--ff-only", $"origin/{milestone.BaseBranch}"], ct);
                baseSha = await RevParseAsync(milestone.BaseBranch, ct);
            }

            await RunAsync(["switch", milestone.BaseBranch], ct);
            var exists = await ExistsAsync($"refs/heads/{milestone.Branch}", ct);
            if (!exists) await RunAsync(["switch", "-c", milestone.Branch, baseSha], ct);
            else
            {
                var branchSha = await RevParseAsync(milestone.Branch, ct);
                if (!await IsAncestorAsync(baseSha, branchSha, ct)) return await HumanAsync(milestone.Id, metadata with { GitFailure = "Existing milestone branch does not descend from the latest developer commit." }, "Existing milestone branch has an unexpected base.", ct);
                await RunAsync(["switch", milestone.Branch], ct);
            }

            metadata = metadata with { BaseCommit = baseSha, WorkingBranch = milestone.Branch, BranchHead = await RevParseAsync("HEAD", ct), WorkingTreeClean = true, GitLifecycleState = GitLifecycleState.BranchReady, GitRecoveryAction = "Resume from persisted branch state if the process stops." };
            await SaveAsync(milestone.Id, metadata, runId, ct);
            return new(true, metadata.GitLifecycleState, $"Milestone branch {milestone.Branch} is ready from {baseSha[..Math.Min(12, baseSha.Length)]}.", metadata);
        }
        catch (HumanRequiredException ex) { return await HumanAsync(milestone.Id, metadata with { GitFailure = ex.Message }, ex.Message, CancellationToken.None); }
        catch (Exception ex) { return await FailedAsync(milestone.Id, metadata with { GitFailure = ex.Message }, ex.Message, CancellationToken.None); }
    }

    public async Task<GitWorkflowResult> FinalizeMilestoneAsync(MilestoneDefinition milestone, bool gatesPassed, string? runId = null, CancellationToken ct = default)
    {
        var metadata = await state.LoadAsync(milestone.Id, ct) ?? NewMetadata(milestone);
        try
        {
            if (!gatesPassed) return await HumanAsync(milestone.Id, metadata, "Milestone gates are not approved; Git finalization is not allowed.", ct);
            await RequireRepositoryAsync(ct);
            await RequireExpectedBranchAsync(milestone.Branch, ct);
            await RequireCleanAndNoOperationInProgressAsync(ct, allowDirty: true);
            if (validation is null) return await HumanAsync(milestone.Id, metadata, "Authoritative final validation is not configured.", ct);
            var results = await validation.RunAsync(ct);
            if (results.Any(x => x.Required && !x.Passed))
            {
                var failed = string.Join(", ", results.Where(x => x.Required && !x.Passed).Select(x => $"{x.Category}: {x.Reason ?? x.Process.StandardError.Trim().Split('\n').LastOrDefault() ?? "unknown"}"));
                return await HumanAsync(milestone.Id, metadata with { PostMergeValidation = false, GitFailure = $"Feature final validation failed: {failed}." }, $"Feature final validation failed ({failed}); no commit or push was performed.", ct);
            }
            metadata = metadata with { GitLifecycleState = GitLifecycleState.Validated, WorkingTreeClean = false };
            await StageMilestoneFilesAsync(ct);
            if (await RunAsync(["diff", "--cached", "--quiet"], ct, acceptFailure: true) is { ExitCode: 1 })
            {
                await RunAsync(["commit", "-m", milestone.CommitMessage ?? $"feat({milestone.Id.ToLowerInvariant()}): complete {milestone.Title.ToLowerInvariant()}"], ct);
                metadata = metadata with { CommitTimestamp = DateTimeOffset.UtcNow, GitLifecycleState = GitLifecycleState.Committed };
            }
            metadata = metadata with { BranchHead = await RevParseAsync("HEAD", ct), WorkingTreeClean = string.IsNullOrEmpty(await StatusAsync(ct)) };
            await SaveAsync(milestone.Id, metadata, runId, ct);
            var remoteFeature = await TryRevParseAsync($"origin/{milestone.Branch}", ct);
            if (remoteFeature != metadata.BranchHead)
            {
                await RunAsync(["push", "-u", "origin", milestone.Branch], ct);
                remoteFeature = await RevParseAsync($"origin/{milestone.Branch}", ct);
                metadata = metadata with { FeaturePushSha = remoteFeature, PushTimestamp = DateTimeOffset.UtcNow, GitLifecycleState = GitLifecycleState.FeaturePushed };
            }
            else metadata = metadata with { FeaturePushSha = remoteFeature, GitLifecycleState = GitLifecycleState.FeaturePushed };

            await SaveAsync(milestone.Id, metadata, runId, ct);
            await RunAsync(["switch", milestone.BaseBranch], ct);
            await RunAsync(["fetch", "origin"], ct);
            var developer = await RevParseAsync(milestone.BaseBranch, ct);
            var remoteDeveloper = await RevParseAsync($"origin/{milestone.BaseBranch}", ct);
            var relation = await RelationAsync(developer, remoteDeveloper, ct);
            if (relation == BranchRelation.Behind) { await RunAsync(["merge", "--ff-only", $"origin/{milestone.BaseBranch}"], ct); developer = await RevParseAsync(milestone.BaseBranch, ct); }
            else if (relation is BranchRelation.Ahead or BranchRelation.Diverged) return await HumanAsync(milestone.Id, metadata, $"{milestone.BaseBranch} is {relation.ToString().ToLowerInvariant()} of origin/{milestone.BaseBranch}.", ct);
            metadata = metadata with { DeveloperPreMergeSha = developer, GitLifecycleState = GitLifecycleState.MergingToDeveloper };
            await SaveAsync(milestone.Id, metadata, runId, ct);
            await RunAsync(["merge", "--no-edit", milestone.Branch], ct);
            metadata = metadata with { MergeCommitSha = await RevParseAsync("HEAD", ct), MergeTimestamp = DateTimeOffset.UtcNow, GitLifecycleState = GitLifecycleState.Merged };
            await SaveAsync(milestone.Id, metadata, runId, ct);
            var postMerge = await validation.RunAsync(ct);
            if (postMerge.Any(x => x.Required && !x.Passed))
            {
                var failed = string.Join(", ", postMerge.Where(x => x.Required && !x.Passed).Select(x => $"{x.Category}: {x.Reason ?? x.Process.StandardError.Trim().Split('\n').LastOrDefault() ?? "unknown"}"));
                return await HumanAsync(milestone.Id, metadata with { PostMergeValidation = false, GitFailure = $"Developer post-merge validation failed: {failed}." }, $"Developer post-merge validation failed ({failed}); developer was not pushed.", ct);
            }
            metadata = metadata with { PostMergeValidation = true, DeveloperPostMergeSha = await RevParseAsync("HEAD", ct), GitLifecycleState = GitLifecycleState.PostMergeValidated };
            await RunAsync(["push", "origin", milestone.BaseBranch], ct);
            var originAfter = await RevParseAsync($"origin/{milestone.BaseBranch}", ct);
            metadata = metadata with { OriginDeveloperSha = originAfter, GitLifecycleState = GitLifecycleState.Published, PushTimestamp = DateTimeOffset.UtcNow };
            await SaveAsync(milestone.Id, metadata, runId, ct);
            return new(true, metadata.GitLifecycleState, "Milestone published to developer.", metadata);
        }
        catch (HumanRequiredException ex) { return await HumanAsync(milestone.Id, metadata with { GitFailure = ex.Message }, ex.Message, CancellationToken.None); }
        catch (Exception ex) { return await FailedAsync(milestone.Id, metadata with { GitFailure = ex.Message }, ex.Message, CancellationToken.None); }
    }

    private GitLifecycleMetadata NewMetadata(MilestoneDefinition m) => new() { RepositoryRoot = repository, BaseBranch = m.BaseBranch, WorkingBranch = m.Branch, GitLifecycleState = GitLifecycleState.NotPrepared };
    private async Task SaveAsync(string id, GitLifecycleMetadata m, string? runId, CancellationToken ct) => await state.SaveAsync(id, m, ct);
    private async Task RequireRepositoryAsync(CancellationToken ct)
    {
        var result = await RunAsync(["rev-parse", "--show-toplevel"], ct);
        if (string.IsNullOrWhiteSpace(result.StandardOutput) || !Directory.Exists(Path.Combine(repository, ".git")))
            throw new HumanRequiredException("Git repository root does not match the mobile workspace.");
    }
    private async Task RequireExpectedBranchAsync(string expected, CancellationToken ct) { var actual = (await RunAsync(["branch", "--show-current"], ct)).StandardOutput.Trim(); if (!string.Equals(actual, expected, StringComparison.Ordinal)) throw new HumanRequiredException($"Expected branch '{expected}', found '{actual}'."); }
    private async Task RequireCleanAndNoOperationInProgressAsync(CancellationToken ct, bool allowDirty = false) { if (!allowDirty && !string.IsNullOrEmpty(await StatusAsync(ct))) throw new HumanRequiredException("Working tree is dirty; ownership must be resolved by a human."); foreach (var file in new[] { "MERGE_HEAD", "REBASE_HEAD", "CHERRY_PICK_HEAD" }) if (File.Exists(Path.Combine(repository, ".git", file))) throw new HumanRequiredException($"Git operation is in progress: {file}."); }
    private async Task StageMilestoneFilesAsync(CancellationToken ct)
    {
        var paths = new[] { "app", "tools/ai-orchestrator", "ai", "architecture", "docs", "README.md", ".gitignore" }
            .Where(path => Directory.Exists(Path.Combine(repository, path)) || File.Exists(Path.Combine(repository, path)))
            .ToArray();
        await RunAsync(["add", "--all", "--", .. paths], ct);
    }
    private async Task<string> StatusAsync(CancellationToken ct) => (await RunAsync(["status", "--porcelain=v1"], ct)).StandardOutput.Trim();
    private async Task<string> RevParseAsync(string name, CancellationToken ct) => (await RunAsync(["rev-parse", "--verify", name], ct)).StandardOutput.Trim();
    private async Task<string?> TryRevParseAsync(string name, CancellationToken ct) { var r = await RunAsync(["rev-parse", "--verify", name], ct, true); return r.Succeeded ? r.StandardOutput.Trim() : null; }
    private async Task<bool> ExistsAsync(string name, CancellationToken ct) => (await RunAsync(["show-ref", "--verify", "--quiet", name], ct, true)).Succeeded;
    private async Task<bool> IsAncestorAsync(string ancestor, string descendant, CancellationToken ct) => (await RunAsync(["merge-base", "--is-ancestor", ancestor, descendant], ct, true)).Succeeded;
    private async Task<BranchRelation> RelationAsync(string local, string remote, CancellationToken ct) { var localAncestor = await IsAncestorAsync(local, remote, ct); var remoteAncestor = await IsAncestorAsync(remote, local, ct); return localAncestor && remoteAncestor ? BranchRelation.Equal : localAncestor ? BranchRelation.Behind : remoteAncestor ? BranchRelation.Ahead : BranchRelation.Diverged; }
    private async Task<ProcessResult> RunAsync(IReadOnlyList<string> args, CancellationToken ct, bool acceptFailure = false) { if (args.Any(x => x is "--force" or "--force-with-lease" or "--hard" or "clean" or "rebase" or "stash" or "-D" or "--amend")) throw new InvalidOperationException("Prohibited Git operation requested."); var r = await processes.RunAsync(new ProcessSpec("git", args, repository, Timeout: TimeSpan.FromMinutes(5)), ct); if (!acceptFailure && !r.Succeeded) throw new HumanRequiredException($"git {string.Join(' ', args)} failed: {r.StandardError.Trim()}"); return r; }
    private async Task<GitWorkflowResult> HumanAsync(string id, GitLifecycleMetadata m, string summary, CancellationToken ct)
    {
        var result = new GitWorkflowResult(false, GitLifecycleState.HumanRequired, summary, m with { GitLifecycleState = GitLifecycleState.HumanRequired });
        await state.SaveAsync(id, result.Metadata, ct);
        return result;
    }

    private async Task<GitWorkflowResult> FailedAsync(string id, GitLifecycleMetadata m, string summary, CancellationToken ct)
    {
        var result = new GitWorkflowResult(false, GitLifecycleState.Failed, summary, m with { GitLifecycleState = GitLifecycleState.Failed });
        await state.SaveAsync(id, result.Metadata, ct);
        return result;
    }
    private enum BranchRelation { Equal, Ahead, Behind, Diverged }
    private sealed class HumanRequiredException(string message) : Exception(message);
}
