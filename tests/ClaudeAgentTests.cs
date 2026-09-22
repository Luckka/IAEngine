using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Agents;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Models;

namespace OnlineOs.AiOrchestrator.Tests;

public sealed class ClaudeAgentTests
{
    [Fact]
    public async Task InvocationUsesConfiguredAllowedToolsAndSendsPromptThroughStandardInput()
    {
        var process = new CapturingProcessRunner(SuccessResponse());
        var options = new ClaudeAgentOptions
        {
            Arguments = ["-p", "--output-format", "json"],
            AllowedTools = ["Read", "Edit", "Glob"]
        };
        var agent = new ClaudeAgent(process, options, "/repo", TimeSpan.FromMinutes(1));

        var result = await agent.ImplementAsync(TestData.Task(), TestData.Route(), TestData.Engineering());

        Assert.True(result.Success);
        Assert.Equal(["-p", "--output-format", "json", "--allowedTools", "Read", "Edit", "Glob"], process.Spec!.Arguments);
        Assert.Equal(Path.GetFullPath("/repo"), process.Spec.WorkingDirectory);
        Assert.NotNull(process.Spec.StandardInput);
        Assert.Contains("Implement task TEST-1", process.Spec.StandardInput);
    }

    [Fact]
    public async Task ExitCodeZeroWithRequiredImplementationDenialFails()
    {
        const string response = """
            {"result":"{\"success\":true,\"summary\":\"edited\",\"filesChanged\":[\"file.md\"]}","permission_denials":[{"tool_name":"Edit"}]}
            """;
        var agent = CreateAgent(new ProcessResult("claude", 0, response, "", TimeSpan.Zero));

        var result = await agent.ImplementAsync(TestData.Task(), TestData.Route(), TestData.Engineering());

        Assert.False(result.Success);
        Assert.Equal(ClaudeAgent.PermissionDeniedReason, result.Summary);
        Assert.Empty(result.FilesChanged);
        Assert.True(Assert.Single(result.PermissionDenials!).Blocking);
    }

    [Fact]
    public async Task ExitCodeZeroWithSuccessfulImplementationAndOptionalGitDenialSucceedsWithWarning()
    {
        const string response = """
            {"result":"{\"success\":true,\"summary\":\"edited\",\"filesChanged\":[\"file.md\"]}","permission_denials":[{"tool_name":"Bash","input":{"command":"git diff --stat"}}]}
            """;
        var agent = CreateAgent(new ProcessResult("claude", 0, response, "", TimeSpan.Zero));

        var result = await agent.ImplementAsync(TestData.Task(), TestData.Route(), TestData.Engineering());

        Assert.True(result.Success);
        Assert.False(Assert.Single(result.PermissionDenials!).Blocking);
        Assert.NotEmpty(result.Warnings!);
    }

    [Fact]
    public async Task ClaudeUnableToCompleteWithPermissionDenialFails()
    {
        const string response = """
            {"result":"{\"success\":false,\"summary\":\"Unable to edit because permission was denied\",\"filesChanged\":[]}","permission_denials":[{"tool_name":"Bash","input":{"command":"git status --short"}}]}
            """;
        var agent = CreateAgent(new ProcessResult("claude", 0, response, "", TimeSpan.Zero));

        var result = await agent.ImplementAsync(TestData.Task(), TestData.Route(), TestData.Engineering());

        Assert.False(result.Success);
        Assert.Equal(ClaudeAgent.PermissionDeniedReason, result.Summary);
    }

    [Fact]
    public async Task ValidResponseWithoutPermissionDenialsSucceeds()
    {
        var agent = CreateAgent(SuccessResponse());

        var result = await agent.ImplementAsync(TestData.Task(), TestData.Route(), TestData.Engineering());

        Assert.True(result.Success);
        Assert.Equal("edited", result.Summary);
        Assert.Equal(["file.md"], result.FilesChanged);
        Assert.Equal(["file_test.md"], result.TestsChanged);
        Assert.Equal("not a UI task", result.NotApplicable!["responsiveDesign"]);
    }

    [Fact]
    public void DefaultClaudeToolsIncludeBashWithoutRemovingExistingTools()
    {
        var tools = new ClaudeAgentOptions().AllowedTools;

        Assert.Equal(["Read", "Edit", "Write", "Glob", "Grep", "Bash"], tools);
    }

    [Fact]
    public async Task DefaultInvocationIncludesBashAndNoPermissionBypassFlags()
    {
        var process = new CapturingProcessRunner(SuccessResponse());
        var agent = new ClaudeAgent(process, new ClaudeAgentOptions(), "/repo", TimeSpan.FromMinutes(1));

        await agent.ImplementAsync(TestData.Task(), TestData.Route(), TestData.Engineering());

        Assert.Contains("Bash", process.Spec!.Arguments);
        Assert.DoesNotContain("--dangerously-skip-permissions", process.Spec.Arguments);
        Assert.DoesNotContain("--allow-dangerously-skip-permissions", process.Spec.Arguments);
        Assert.DoesNotContain("bypassPermissions", process.Spec.Arguments);
    }

    [Fact]
    public async Task StructuredRefusalIsDistinguishableFromMalformedOutput()
    {
        const string response = """
            {"result":"{\"success\":false,\"summary\":\"CLAUDE_IMPLEMENTATION_REFUSED\",\"failureCode\":\"CLAUDE_IMPLEMENTATION_REFUSED\",\"structuredRefusal\":true,\"refusalReason\":\"The supplied policy profile contradicts the executable Flutter task.\",\"filesChanged\":[],\"testsChanged\":[],\"notApplicable\":{}}","permission_denials":[]}
            """;
        var agent = CreateAgent(new ProcessResult("claude", 0, response, "", TimeSpan.Zero));

        var result = await agent.ImplementAsync(TestData.Task(), TestData.Route(), TestData.Engineering());

        Assert.False(result.Success);
        Assert.Equal(ClaudeAgent.StructuredRefusalReason, result.Summary);
        Assert.Equal(ClaudeAgent.StructuredRefusalReason, result.FailureCode);
        Assert.True(result.StructuredRefusal);
        Assert.Contains("contradicts", result.Warnings!.Single());
    }

    [Fact]
    public async Task MalformedClaudeOutputRemainsDistinctFromStructuredRefusal()
    {
        var agent = CreateAgent(new ProcessResult("claude", 0, "Claude cannot safely proceed in prose.", "", TimeSpan.Zero));

        var result = await agent.ImplementAsync(TestData.Task(), TestData.Route(), TestData.Engineering());

        Assert.False(result.Success);
        Assert.Equal("Claude returned malformed structured output.", result.Summary);
        Assert.False(result.StructuredRefusal);
        Assert.Null(result.FailureCode);
    }

    [Fact]
    public async Task PromptTooLongIsParsedFromStructuredStdoutBeforeExitCodeClassification()
    {
        var process = new ProcessResult("claude", 1,
            "{\"terminal_reason\":\"prompt_too_long\",\"api_error_status\":400,\"result\":\"request too large\"}", "", TimeSpan.Zero);
        var result = await CreateAgent(process).ImplementAsync(TestData.Task(), TestData.Route(), TestData.Engineering());

        Assert.False(result.Success);
        Assert.Equal(ClaudeAgent.PromptTooLongReason, result.FailureCode);
        Assert.Equal(400, result.Process!.ProviderHttpStatus);
    }

    private static ClaudeAgent CreateAgent(ProcessResult result) => new(
        new CapturingProcessRunner(result),
        new ClaudeAgentOptions(),
        "/repo",
        TimeSpan.FromMinutes(1));

    private static ProcessResult SuccessResponse() => new(
        "claude",
        0,
        """{"result":"{\"success\":true,\"summary\":\"edited\",\"filesChanged\":[\"file.md\"],\"testsChanged\":[\"file_test.md\"],\"notApplicable\":{\"responsiveDesign\":\"not a UI task\"}}","permission_denials":[]}""",
        "",
        TimeSpan.Zero);

    private sealed class CapturingProcessRunner(ProcessResult result) : IProcessRunner
    {
        public ProcessSpec? Spec { get; private set; }

        public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken = default)
        {
            Spec = spec;
            return Task.FromResult(result);
        }
    }
}
