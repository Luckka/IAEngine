using OnlineOs.AiOrchestrator.Models;

namespace OnlineOs.AiOrchestrator.Hosting;

/// <summary>
/// Optional consumer-owned source of additional task context. The Core does not
/// know which repository, product or technology supplies the context.
/// </summary>
public interface IEngineReferenceContextProvider
{
    bool IsApplicable(DevelopmentTask task);
    Task<EngineReferenceContext> InspectAsync(DevelopmentTask task, CancellationToken cancellationToken = default);
}

public sealed record EngineReferenceContext(
    string Context,
    object? Artifact = null,
    string ArtifactName = "reference-context.json");
