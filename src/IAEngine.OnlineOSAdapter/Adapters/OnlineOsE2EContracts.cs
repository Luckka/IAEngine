using OnlineOs.AiOrchestrator.Models;

namespace OnlineOs.AiOrchestrator.Abstractions;

/// <summary>OnlineOS adapter contracts for device-backed QA.</summary>
public interface IE2ETestRunner
{
    Task<E2ETestResult> RunAsync(E2ETestRequest request, CancellationToken cancellationToken = default);
}

public interface IE2EDeviceManager
{
    Task<E2EDeviceResult> EnsureReadyAsync(E2ETestRequest request, CancellationToken cancellationToken = default);
}
