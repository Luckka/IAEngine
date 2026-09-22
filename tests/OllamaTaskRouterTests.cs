using System.Net;
using System.Text;
using OnlineOs.AiOrchestrator.Agents;
using OnlineOs.AiOrchestrator.Configuration;

namespace OnlineOs.AiOrchestrator.Tests;

public sealed class OllamaTaskRouterTests
{
    [Fact]
    public async Task MalformedResponseUsesDeterministicFallback()
    {
        using var http = Client("{\"response\":\"not-json\"}");
        var result = await new OllamaTaskRouter(http, new OllamaOptions { Model = "test", MaxResponseRetries = 0 }).RouteAsync(TestData.Task());
        Assert.True(result.UsedFallback);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public async Task UnavailableOllamaUsesDeterministicFallback()
    {
        using var http = new HttpClient(new StubHandler(_ => throw new HttpRequestException("offline"))) { BaseAddress = new Uri("http://localhost/") };
        var result = await new OllamaTaskRouter(http, new OllamaOptions { Model = "test", MaxResponseRetries = 0 }).RouteAsync(TestData.Task());
        Assert.True(result.UsedFallback);
    }

    [Fact]
    public async Task ValidResponseIsAccepted()
    {
        var route = "{\\\"type\\\":\\\"feature\\\",\\\"domains\\\":[\\\"offline\\\"],\\\"complexity\\\":\\\"high\\\",\\\"risk\\\":\\\"high\\\",\\\"skills\\\":[],\\\"relevantContext\\\":[],\\\"recommendedPipeline\\\":\\\"claude-codex\\\",\\\"reasoningSummary\\\":\\\"Offline work needs independent review.\\\"}";
        using var http = Client($"{{\"response\":\"{route}\"}}");
        var result = await new OllamaTaskRouter(http, new OllamaOptions { Model = "test", MaxResponseRetries = 0 }).RouteAsync(TestData.Task());
        Assert.False(result.UsedFallback);
    }

    [Fact]
    public async Task InvalidEnumUsesFallback()
    {
        var route = "{\\\"type\\\":\\\"feature\\\",\\\"domains\\\":[\\\"offline\\\"],\\\"complexity\\\":\\\"extreme\\\",\\\"risk\\\":\\\"high\\\",\\\"skills\\\":[],\\\"relevantContext\\\":[],\\\"recommendedPipeline\\\":\\\"claude-codex\\\",\\\"reasoningSummary\\\":\\\"Invalid complexity.\\\"}";
        using var http = Client($"{{\"response\":\"{route}\"}}");
        var result = await new OllamaTaskRouter(http, new OllamaOptions { Model = "test", MaxResponseRetries = 0 }).RouteAsync(TestData.Task());
        Assert.True(result.UsedFallback);
    }

    [Fact]
    public async Task SensitiveDomainsAreElevatedByDeterministicPolicy()
    {
        var route = "{\\\"type\\\":\\\"feature\\\",\\\"domains\\\":[\\\"offline\\\",\\\"sync\\\"],\\\"complexity\\\":\\\"medium\\\",\\\"risk\\\":\\\"medium\\\",\\\"skills\\\":[],\\\"relevantContext\\\":[\\\"made-up.md\\\",\\\"docs/OFFLINE_SYNC.md\\\"],\\\"recommendedPipeline\\\":\\\"local-only\\\",\\\"reasoningSummary\\\":\\\"Offline sync.\\\"}";
        using var http = Client($"{{\"response\":\"{route}\"}}");
        var result = await new OllamaTaskRouter(http, new OllamaOptions { Model = "test", MaxResponseRetries = 0 }).RouteAsync(TestData.Task());
        Assert.Equal("high", result.Risk);
        Assert.Equal("claude-codex", result.RecommendedPipeline);
        Assert.DoesNotContain("made-up.md", result.RelevantContext);
    }

    private static HttpClient Client(string response) => new(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(response, Encoding.UTF8, "application/json")
    })) { BaseAddress = new Uri("http://localhost/") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response(request));
    }
}
