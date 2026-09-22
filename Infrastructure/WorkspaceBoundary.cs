namespace OnlineOs.AiOrchestrator.Infrastructure;

/// <summary>
/// Path-aware containment checks for orchestrator-managed repository paths.
/// This is not an OS sandbox for arbitrary shell commands.
/// </summary>
public sealed class WorkspaceBoundary
{
    public WorkspaceBoundary(string repositoryRoot)
    {
        var fullRoot = Path.GetFullPath(repositoryRoot);
        var rootInfo = new DirectoryInfo(fullRoot);
        var resolvedRoot = rootInfo.ResolveLinkTarget(returnFinalTarget: true);
        Root = (resolvedRoot as DirectoryInfo)?.FullName ?? fullRoot;
        if (!Directory.Exists(Root)) throw new DirectoryNotFoundException($"Workspace root does not exist: {Root}");
    }

    public string Root { get; }

    public bool Contains(string path)
    {
        var candidate = Path.GetFullPath(path, Root);
        var relative = Path.GetRelativePath(Root, candidate);
        if (relative == ".") return true;
        if (Path.IsPathRooted(relative)) return false;
        var firstSegment = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        if (firstSegment is ".." or "") return false;
        return !HasSymlinkEscape(candidate);
    }

    public string Resolve(string path)
    {
        var candidate = Path.GetFullPath(path, Root);
        if (!Contains(candidate)) throw new InvalidOperationException($"Path escapes the OnlineOS workspace: {path}");
        return candidate;
    }

    private bool HasSymlinkEscape(string candidate)
    {
        var relative = Path.GetRelativePath(Root, candidate);
        var current = Root;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (!string.IsNullOrWhiteSpace(info.LinkTarget))
            {
                var target = Path.GetFullPath(info.LinkTarget, Path.GetDirectoryName(current)!);
                if (!ContainsWithoutSymlink(target)) return true;
                current = target;
            }
        }
        return false;
    }

    private bool ContainsWithoutSymlink(string path)
    {
        var relative = Path.GetRelativePath(Root, Path.GetFullPath(path));
        return relative == "." || (!Path.IsPathRooted(relative) && relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0] is not (".." or ""));
    }
}
