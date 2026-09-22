using System.Diagnostics;
using OnlineOs.AiOrchestrator.Configuration;

namespace OnlineOs.AiOrchestrator.Pipeline;

public sealed record PatrolExecutableResolution(bool Available, string? ExecutablePath, string Detail);

public interface IPatrolExecutableResolver
{
    PatrolExecutableResolution Resolve();
}

/// <summary>Resolves Patrol once for a QA run so every profile uses an absolute executable.</summary>
public sealed class PatrolExecutableResolver(QaOptions options, string repository, string? path = null, Func<string, string?>? shellLookup = null) : IPatrolExecutableResolver
{
    public PatrolExecutableResolution Resolve()
    {
        var configured = options.PatrolCommand.Trim();
        if (configured.Length == 0)
            return Missing("Patrol CLI is not configured.");

        var candidates = new List<string>();
        if (Path.IsPathRooted(configured)) candidates.Add(configured);
        else if (configured.Contains(Path.DirectorySeparatorChar) || configured.Contains(Path.AltDirectorySeparatorChar))
            candidates.Add(Path.GetFullPath(Path.Combine(repository, configured)));

        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var pubCache = Environment.GetEnvironmentVariable("PUB_CACHE");
        if (!string.IsNullOrWhiteSpace(pubCache)) candidates.Add(Path.Combine(pubCache, "bin", configured));
        if (!string.IsNullOrWhiteSpace(userHome)) candidates.Add(Path.Combine(userHome, ".pub-cache", "bin", configured));

        var pathValue = path ?? Environment.GetEnvironmentVariable("PATH") ?? "";
        candidates.AddRange(pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, configured)));

        var shellPath = shellLookup?.Invoke(configured) ?? (OperatingSystem.IsMacOS() ? LookupWithShell(configured) : null);
        if (!string.IsNullOrWhiteSpace(shellPath)) candidates.Add(shellPath);

        foreach (var candidate in candidates.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal))
        {
            if (File.Exists(candidate)) return new(true, candidate, "Patrol CLI resolved.");
        }

        return Missing($"Patrol executable was not found for configured command '{configured}'.");
    }

    private static PatrolExecutableResolution Missing(string detail) => new(false, null, detail);

    private static string? LookupWithShell(string command)
    {
        try
        {
            var info = new ProcessStartInfo("/bin/zsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            info.ArgumentList.Add("-lc");
            info.ArgumentList.Add($"command -v {ShellQuote(command)}");
            using var process = Process.Start(info);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 && Path.IsPathRooted(output) ? output : null;
        }
        catch { return null; }
    }

    private static string ShellQuote(string value) => $"'{value.Replace("'", "'\\''")}'";
}
