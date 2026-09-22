using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Models;

namespace OnlineOs.AiOrchestrator.Infrastructure;

public sealed class GitService(IProcessRunner processes, string repository, GitOptions options) : IGitService
{
    private string Repository { get; } = new WorkspaceBoundary(repository).Root;
    public Task<string> GetRootAsync(CancellationToken ct = default) => ReadAsync(["rev-parse", "--show-toplevel"], ct);
    public Task<string> GetBranchAsync(CancellationToken ct = default) => ReadAsync(["branch", "--show-current"], ct);
    public Task<string> GetStatusAsync(CancellationToken ct = default) => ReadAsync(["status", "--short"], ct);
    public async Task<string> GetDiffAsync(CancellationToken ct = default)
    {
        var tracked = await ReadAsync(["diff", "--no-ext-diff", "HEAD", "--", "."], ct);
        var untracked = await ReadAsync(["ls-files", "--others", "--exclude-standard"], ct);
        var sections = new List<string> { tracked };
        foreach (var file in untracked.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var safeFile = new WorkspaceBoundary(Repository).Resolve(file);
            var result = await processes.RunAsync(new ProcessSpec("git", ["diff", "--no-index", "--", "/dev/null", safeFile], Repository, Timeout: TimeSpan.FromSeconds(30)), ct);
            if (result.ExitCode is 0 or 1) sections.Add(result.StandardOutput);
            else sections.Add($"Untracked file could not be rendered: {file}: {result.StandardError}");
        }
        return string.Join(Environment.NewLine, sections.Where(x => !string.IsNullOrWhiteSpace(x)));
    }
    public Task<string> GetCommitAsync(CancellationToken ct = default) => ReadAsync(["rev-parse", "HEAD"], ct);

    public async Task<(bool Safe, string Reason)> CheckBranchSafetyAsync(CancellationToken ct = default)
    {
        var branch = await GetBranchAsync(ct);
        if (string.IsNullOrWhiteSpace(branch)) return (false, "Detached HEAD is not allowed for implementation runs.");
        var permanentlyProtected = branch is "developer" or "producao";
        if (permanentlyProtected || (!options.AllowProtectedBranch && options.ProtectedBranches.Contains(branch, StringComparer.OrdinalIgnoreCase)))
            return (false, $"Branch '{branch}' is protected. Create or switch to a feature/work branch.");
        return (true, $"Branch '{branch}' is allowed.");
    }

    private async Task<string> ReadAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var result = await processes.RunAsync(new ProcessSpec("git", arguments, Repository, Timeout: TimeSpan.FromSeconds(30)), ct);
        if (!result.Succeeded) throw new InvalidOperationException($"Git command failed: {result.StandardError.Trim()}");
        return result.StandardOutput.Trim();
    }
}
