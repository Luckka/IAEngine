using System.Text.Json;
using System.Text.RegularExpressions;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Infrastructure;
using OnlineOs.AiOrchestrator.Hosting;

namespace OnlineOs.AiOrchestrator.Reference;

public enum CompatibilityClassification { CONFIRMED, ADAPTED, BACKEND_GAP, CONFLICT, UNKNOWN, DEFERRED }

public sealed record ReferenceFinding(string Concept, string Source, string? BackendType, CompatibilityClassification Classification, string? Description = null);
public sealed record BackendGap(string Id, string Description, string FrontendDecision, string FutureAction, CompatibilityClassification Classification = CompatibilityClassification.DEFERRED);

public sealed class BackendReferenceArtifact
{
    public string TaskId { get; init; } = "";
    public string ReferenceRepository { get; init; } = "onlineos";
    public string RepositoryType { get; init; } = "Monolith";
    public string ReferenceBranch { get; init; } = "b1208";
    public string ReferenceCommit { get; init; } = "";
    public bool ReferenceAvailable { get; init; }
    public bool ReferenceWorkingTreeDirty { get; init; }
    public string ReferenceStatus { get; init; } = "UNAVAILABLE";
    public List<string> InspectedFiles { get; init; } = [];
    public List<string> ContractSources { get; init; } = [];
    public List<string> LegacyFrontendSources { get; init; } = [];
    public List<ReferenceFinding> ConfirmedContracts { get; init; } = [];
    public List<ReferenceFinding> Adaptations { get; init; } = [];
    public List<BackendGap> BackendGaps { get; init; } = [];
    public List<BackendGap> Conflicts { get; init; } = [];
    public List<BackendGap> Unknowns { get; init; } = [];
    public List<BackendGap> DeferredItems { get; init; } = [];
    public List<string> Assumptions { get; init; } = [];
    public bool HumanRequired { get; init; }
    public string? ErrorCode { get; init; }
}

public sealed class ReferenceIndex
{
    public string Repository { get; init; } = "onlineos";
    public string Type { get; init; } = "monolith";
    public string Branch { get; init; } = "b1208";
    public string Commit { get; init; } = "";
    public string SourceRevision => Commit;
    public bool WorkingTreeDirty { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
    public List<string> BackendContractRoots { get; init; } = [];
    public List<string> BackendApplicationRoots { get; init; } = [];
    public List<string> BackendDomainRoots { get; init; } = [];
    public List<string> InfrastructureRoots { get; init; } = [];
    public List<string> LegacyFrontendRoots { get; init; } = [];
    public List<string> UnknownRoots { get; init; } = [];
}

public interface IReferenceInspector
{
    Task<BackendReferenceArtifact> InspectAsync(DevelopmentTask task, CancellationToken cancellationToken = default);
}

/// <summary>
/// OnlineOS adapter for the technology-neutral Core reference-context contract.
/// The Core receives only the resulting context and opaque artifact.
/// </summary>
public sealed class OnlineOsReferenceContextProvider(IReferenceInspector inspector) : IEngineReferenceContextProvider
{
    public bool IsApplicable(DevelopmentTask task) =>
        task.Id.StartsWith("M2", StringComparison.OrdinalIgnoreCase)
        || task.Id.StartsWith("M3", StringComparison.OrdinalIgnoreCase)
        || task.Id.StartsWith("M4", StringComparison.OrdinalIgnoreCase)
        || task.Id.StartsWith("M5", StringComparison.OrdinalIgnoreCase)
        || (task.Domains?.Contains("flutter", StringComparer.OrdinalIgnoreCase) == true && task.Domains.Contains("milestone", StringComparer.OrdinalIgnoreCase));

    public async Task<EngineReferenceContext> InspectAsync(DevelopmentTask task, CancellationToken cancellationToken = default)
    {
        var artifact = await inspector.InspectAsync(task, cancellationToken);
        return new EngineReferenceContext(BuildSummary(artifact), artifact, "backend-reference.json");
    }

    public static string BuildSummary(BackendReferenceArtifact artifact)
    {
        var confirmed = artifact.ConfirmedContracts.Take(12).Select(x => $"- {x.Concept}: {x.BackendType ?? "public contract source"} ({x.Classification})");
        var deferred = artifact.DeferredItems.Concat(artifact.BackendGaps).Take(8).Select(x => $"- {x.Description} Keep behind repository abstraction; {x.FutureAction}");
        return $"REFERENCE SYSTEM: OnlineOS monolith\nBranch: {artifact.ReferenceBranch}\nCommit: {artifact.ReferenceCommit}\nStatus: {artifact.ReferenceStatus}\nRelevant confirmed/selected contracts:\n{string.Join("\n", confirmed.DefaultIfEmpty("- None confirmed; use product docs and local mocks."))}\nDeferred compatibility:\n{string.Join("\n", deferred.DefaultIfEmpty("- None."))}\nIMPLEMENTATION RULES: Flutter remains runtime-independent from backend; use mocks/local data; no real HTTP integration; do not modify the reference repository; legacy frontend is secondary evidence only.";
    }
}

/// <summary>Containment and command policy for the separate, read-only monolith root.</summary>
public sealed class ReadOnlyReferenceBoundary
{
    public ReadOnlyReferenceBoundary(string root)
    {
        var full = Path.GetFullPath(root);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException($"Reference root does not exist: {full}");
        Root = (new DirectoryInfo(full).ResolveLinkTarget(true) as DirectoryInfo)?.FullName ?? full;
    }
    public string Root { get; }

    public bool Contains(string path)
    {
        var candidate = Path.GetFullPath(path, Root);
        var relative = Path.GetRelativePath(Root, candidate);
        if (Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0] is ".." or "") return relative == ".";
        var current = Root;
        foreach (var part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (!string.IsNullOrWhiteSpace(info.LinkTarget))
            {
                var target = Path.GetFullPath(info.LinkTarget!, Path.GetDirectoryName(current)!);
                if (!IsWithinResolvedRoot(target)) return false;
                current = target;
            }
        }
        return true;
    }

    public string Resolve(string path)
    {
        var resolved = Path.GetFullPath(path, Root);
        if (!Contains(resolved)) throw new InvalidOperationException($"Path escapes read-only reference root: {path}");
        return resolved;
    }

    public static bool IsSafeReadCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        var c = command.Trim();
        if (c.IndexOfAny([';', '&', '|', '>', '<', '`', '\n', '\r']) >= 0) return false;
        if (Regex.IsMatch(c, @"(?i)(^|\s)(checkout|switch|reset|clean|commit|pull|fetch|merge|rebase|stash|clone|mv|cp|rm|touch|mkdir|install|migrate)(\s|$)")) return false;
        return Regex.IsMatch(c, @"^(git\s+-C\s+\S+\s+(branch\s+--show-current|rev-parse\s+HEAD|status(?:\s+--porcelain)?|ls-files|log|show)|(?:rg|grep|find|ls|head|sed|awk|cat|pwd)(\s|$))");
    }

    public void EnsureSafeReadCommand(string command)
    {
        if (!IsSafeReadCommand(command)) throw new InvalidOperationException("REFERENCE_READ_ONLY_VIOLATION");
    }

    private bool IsWithinResolvedRoot(string path)
    {
        var relative = Path.GetRelativePath(Root, Path.GetFullPath(path));
        return relative == "." || (!Path.IsPathRooted(relative) && relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0] is not (".." or ""));
    }
}

public sealed class MonolithReferenceInspector(IProcessRunner processes, string writableRoot, ReferenceOptions options) : IReferenceInspector
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private readonly string indexPath = new WorkspaceBoundary(writableRoot).Resolve(options.IndexPath);
    private readonly string? configuredRoot = string.IsNullOrWhiteSpace(options.Root) ? Environment.GetEnvironmentVariable("ONLINEOS_REFERENCE_ROOT") : options.Root;

    public async Task<BackendReferenceArtifact> InspectAsync(DevelopmentTask task, CancellationToken cancellationToken = default)
    {
        if (!options.Enabled || string.IsNullOrWhiteSpace(configuredRoot) || !Directory.Exists(configuredRoot))
            return Unavailable(task.Id, "REFERENCE_UNAVAILABLE");

        var boundary = new ReadOnlyReferenceBoundary(configuredRoot);
        var branch = await GitReadAsync(boundary, "branch --show-current", cancellationToken);
        if (!string.Equals(branch.Trim(), options.RequiredBranch, StringComparison.Ordinal))
            return Unavailable(task.Id, "REFERENCE_BRANCH_MISMATCH", branch.Trim(), options.RequiredBranch);
        var commit = (await GitReadAsync(boundary, "rev-parse HEAD", cancellationToken)).Trim();
        var dirty = !string.IsNullOrWhiteSpace(await GitReadAsync(boundary, "status --porcelain", cancellationToken));
        var index = await LoadOrDiscoverAsync(boundary, branch.Trim(), commit, dirty, cancellationToken);
        var relevant = SelectRelevant(index, task);
        var artifact = new BackendReferenceArtifact
        {
            TaskId = task.Id,
            ReferenceAvailable = true,
            ReferenceStatus = "INSPECTED",
            ReferenceBranch = branch.Trim(),
            ReferenceCommit = commit,
            ReferenceWorkingTreeDirty = dirty,
            InspectedFiles = relevant,
            ContractSources = relevant.Where(IsContract).ToList(),
            LegacyFrontendSources = relevant.Where(IsLegacyFrontend).ToList(),
            Assumptions = ["Public API definitions and DTOs take precedence over domain, database, and legacy frontend evidence."],
            DeferredItems = [new BackendGap("REFERENCE-SCOPE", "Backend integration is out of scope for M2-M5.", "Use repository abstractions with mock/local data.", "Revalidate during M6.")]
        };
        foreach (var source in artifact.ContractSources.Take(20)) artifact.Unknowns.Add(new BackendGap(
            $"UNKNOWN-{artifact.Unknowns.Count + 1}",
            $"Static public-contract candidate selected at {source}; exact DTO/endpoint semantics were not inferred by bounded discovery.",
            "Keep the Flutter model behind a repository abstraction and align with product documentation.",
            "Inspect the specific public contract during the task or M6.",
            CompatibilityClassification.UNKNOWN));
        return artifact;
    }

    private async Task<ReferenceIndex> LoadOrDiscoverAsync(ReadOnlyReferenceBoundary boundary, string branch, string commit, bool dirty, CancellationToken ct)
    {
        if (File.Exists(indexPath))
        {
            try
            {
                var old = JsonSerializer.Deserialize<ReferenceIndex>(await File.ReadAllTextAsync(indexPath, ct), JsonOptions);
                if (old is not null && old.Branch == branch && old.Commit == commit && old.Type == "monolith") return old;
            }
            catch (JsonException) { }
        }
        var index = Discover(boundary, branch, commit, dirty);
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        var tmp = indexPath + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(index, JsonOptions), ct);
        File.Move(tmp, indexPath, true);
        return index;
    }

    public static ReferenceIndex Discover(ReadOnlyReferenceBoundary boundary, string branch, string commit, bool dirty = false)
    {
        var result = new ReferenceIndex { Branch = branch, Commit = commit, WorkingTreeDirty = dirty, GeneratedAt = DateTimeOffset.UtcNow };
        foreach (var entry in Directory.EnumerateFileSystemEntries(boundary.Root).OrderBy(x => x).Take(250))
        {
            var relative = Path.GetRelativePath(boundary.Root, entry).Replace(Path.DirectorySeparatorChar, '/');
            if (Directory.Exists(entry))
            {
                var name = Path.GetFileName(entry).ToLowerInvariant();
                if (name.Contains("front") || name is "web" or "client" or "ui") result.LegacyFrontendRoots.Add(relative);
                else if (name.Contains("infra") || name.Contains("migration") || name.Contains("database") || name.Contains("persistence")) result.InfrastructureRoots.Add(relative);
                else if (name.Contains("domain") || name.Contains("application") || name.Contains("core")) result.BackendDomainRoots.Add(relative);
                else if (name.Contains("api") || name.Contains("server") || name.Contains("backend")) result.BackendApplicationRoots.Add(relative);
                else result.UnknownRoots.Add(relative);
            }
            else if (IsContract(relative)) result.BackendContractRoots.Add(relative);
            else result.UnknownRoots.Add(relative);
        }
        return result;
    }

    private static List<string> SelectRelevant(ReferenceIndex i, DevelopmentTask task)
    {
        var all = i.BackendContractRoots.Concat(i.BackendApplicationRoots).Concat(i.BackendDomainRoots).Concat(i.InfrastructureRoots).Concat(i.LegacyFrontendRoots).ToList();
        var terms = (task.Title + " " + task.Description + " " + string.Join(' ', task.RelevantContext ?? [])).Split([' ', '-', '_', '/'], StringSplitOptions.RemoveEmptyEntries).Where(x => x.Length > 3).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return all.Where(x => terms.Any(t => x.Contains(t, StringComparison.OrdinalIgnoreCase)) || IsContract(x)).Take(80).ToList();
    }
    private static bool IsContract(string path) => Regex.IsMatch(path, @"(?i)(openapi|swagger|controller|dto|request|response|validator|api|enum)");
    private static bool IsLegacyFrontend(string path) => Regex.IsMatch(path, @"(?i)(frontend|client|web|ui|package\.json|\.tsx?$|\.jsx?$)");
    private async Task<string> GitReadAsync(ReadOnlyReferenceBoundary boundary, string args, CancellationToken ct)
    {
        // Validate the operation independently of the absolute path so spaces in a
        // legitimate user path cannot make a safe inspection fail closed.
        boundary.EnsureSafeReadCommand($"git -C reference {args}");
        var result = await processes.RunAsync(new ProcessSpec("git", ["-C", boundary.Root, .. args.Split(' ', StringSplitOptions.RemoveEmptyEntries)], boundary.Root), ct);
        if (!result.Succeeded) throw new InvalidOperationException($"Reference inspection failed: {result.StandardError.Trim()}");
        return result.StandardOutput;
    }
    private static BackendReferenceArtifact Unavailable(string taskId, string error, string? actual = null, string? expected = null) => new()
    {
        TaskId = taskId,
        ReferenceAvailable = false,
        ReferenceStatus = error,
        ErrorCode = error,
        Assumptions = [actual is null ? "Continue with product documentation and mock/local data." : $"Expected reference branch {expected}; observed {actual}. Do not switch the reference repository."],
        Unknowns = [new BackendGap(error, "Reference evidence is unavailable for this task.", "Continue behind a repository abstraction where safe.", "Reinspect before M6.", CompatibilityClassification.UNKNOWN)]
    };
}

public static class BackendIntegrationAudit
{
    public static async Task<Dictionary<string, object>> AggregateAsync(string repositoryRoot, CancellationToken ct = default)
    {
        var root = new WorkspaceBoundary(repositoryRoot).Resolve(".ai-runs");
        var artifacts = Directory.Exists(root) ? Directory.EnumerateFiles(root, "backend-reference.json", SearchOption.AllDirectories).OrderBy(x => x).ToArray() : [];
        var entries = new List<BackendReferenceArtifact>();
        foreach (var path in artifacts)
        {
            try { var item = JsonSerializer.Deserialize<BackendReferenceArtifact>(await File.ReadAllTextAsync(path, ct)); if (item is not null) entries.Add(item); } catch (JsonException) { }
        }
        var result = new Dictionary<string, object> { ["generatedAt"] = DateTimeOffset.UtcNow, ["sourceArtifacts"] = artifacts.Select(x => Path.GetRelativePath(repositoryRoot, x)).ToArray(), ["items"] = entries };
        var state = new WorkspaceBoundary(repositoryRoot).Resolve(".ai-state/backend-integration-audit.json");
        Directory.CreateDirectory(Path.GetDirectoryName(state)!);
        await File.WriteAllTextAsync(state, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }), ct);
        return result;
    }
}
