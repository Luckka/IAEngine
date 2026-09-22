using OnlineOs.AiOrchestrator.Models;

namespace OnlineOs.AiOrchestrator.Pipeline;

public static class PatrolTestSelector
{
    private const string Smoke = "integration_test/smoke/technician_journey_test.dart";
    private const string CustomerSummary = "integration_test/service_orders/customer_summary_test.dart";

    public static IReadOnlyList<string> Select(E2EProfile profile, string? taskText = null, string? milestoneId = null)
    {
        if (profile == E2EProfile.Full) return [Smoke, CustomerSummary]; // Expand by adding journeys, not device × locale cartesian products.
        if (profile == E2EProfile.Milestone) return [Smoke, CustomerSummary];
        var text = taskText ?? string.Empty;
        if (text.Contains("customer", StringComparison.OrdinalIgnoreCase)) return [CustomerSummary];
        if (text.Contains("service order", StringComparison.OrdinalIgnoreCase)
            || text.Contains("agenda", StringComparison.OrdinalIgnoreCase)) return [Smoke];
        return [Smoke];
    }
}
