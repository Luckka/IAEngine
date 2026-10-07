using System.Text.Json;
using System.Text.Json.Serialization;

namespace IAEngine.Core.Recovery;

public sealed class FileRecoveryStore(string repositoryRoot, string stateDirectory = ".ai-state") : IRecoveryStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly SemaphoreSlim gate = new(1, 1);
    private string Root => Path.Combine(repositoryRoot, Path.GetFileName(stateDirectory), "recovery");

    public async Task<RecoveryExecution?> GetAsync(string executionKey, CancellationToken cancellationToken = default)
    {
        var path = PathFor(executionKey);
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<RecoveryExecution>(await File.ReadAllTextAsync(path, cancellationToken), Options);
    }

    public async Task<IReadOnlyList<RecoveryAttempt>> GetAttemptsAsync(string executionKey, CancellationToken cancellationToken = default)
        => (await GetAsync(executionKey, cancellationToken))?.Attempts ?? [];

    public async Task<RecoveryExecution> SaveAsync(RecoveryExecution execution, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        cancellationToken.ThrowIfCancellationRequested();
        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Root);
            var path = PathFor(execution.ExecutionKey);
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(execution, Options), cancellationToken);
            File.Move(temporary, path, true);
            return execution;
        }
        finally { gate.Release(); }
    }

    private string PathFor(string executionKey)
    {
        var fileName = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(executionKey))).ToLowerInvariant();
        return Path.Combine(Root, fileName + ".json");
    }
}
