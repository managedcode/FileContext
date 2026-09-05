using System.ClientModel;
using System.Text.Json;
using ManagedCode.LlmTck.Configuration;
using ManagedCode.LlmTck.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

namespace ManagedCode.FileContext.Tests.LlmTck;

public sealed class FileToolResultProtocolTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Theory]
    [InlineData(FileAccessProvider.ReadFileToolName, "{\"fileName\":\"empty.txt\"}", "\"\"")]
    [InlineData(FileAccessProvider.ReadFileToolName, "{\"fileName\":\"missing.txt\"}", "not found")]
    [InlineData(FileAccessProvider.LsToolName, "{\"directory\":\"empty\"}", "[]")]
    [InlineData(FileAccessProvider.GrepToolName, "{\"regexPattern\":\"absent\",\"directory\":\"\"}", "[]")]
    [InlineData(FileContextToolNames.GetInfo, "{\"path\":\"missing.txt\"}", "not_found")]
    [InlineData(FileContextToolNames.ReadRange, "{\"path\":\"empty.txt\"}", "\"content\": \"\"")]
    [InlineData(FileContextToolNames.ReadRange, "{\"path\":\"first.txt\",\"startLine\":100}", "\"content\": \"\"")]
    [InlineData(FileContextToolNames.ReadRange, "{\"path\":\"missing.txt\"}", "Error: Function failed.")]
    [InlineData(FileContextToolNames.ReadRange, "{\"path\":\"../escape.txt\"}", "Error: Function failed.")]
    [InlineData(FileContextToolNames.SearchMarkdownGraph, "{\"query\":\"absent\"}", "Error: Function failed.")]
    public async Task EmptyOrFailedTool_StillSendsMatchingResult(string toolName, string arguments, string expectedContent)
    {
        var requests = await RunToolLoopAsync(LlmTckToolReplay.CreateResponse("call-empty", toolName, arguments));

        foreach (var request in requests)
        {
            var results = LlmTckToolAssertions.AssertClosedCalls(request, "call-empty");
            output.WriteLine(results[0].GetRawText());
            results[0].GetProperty("content").ValueKind.ShouldBe(JsonValueKind.String);
            results[0].GetProperty("content").GetString()!.ShouldContain(expectedContent);
        }
    }

    [Theory]
    [InlineData("missing.txt", "not_found", false)]
    [InlineData("first.txt", "found", true)]
    public async Task MetadataLookup_PreservesStructuredResultAfterSessionRestore(string path, string status, bool exists)
    {
        var requests = await RunToolLoopAsync(LlmTckToolReplay.CreateResponse(
            "metadata", FileContextToolNames.GetInfo, JsonSerializer.Serialize(new { path })));

        foreach (var request in requests)
        {
            var results = LlmTckToolAssertions.AssertClosedCalls(request, "metadata");
            using var content = JsonDocument.Parse(results[0].GetProperty("content").GetString()!);
            content.RootElement.GetProperty("status").GetString().ShouldBe(status);
            content.RootElement.GetProperty("path").GetString().ShouldBe(path);
            content.RootElement.TryGetProperty("info", out var info).ShouldBe(exists);
            if (exists)
            {
                info.GetProperty("path").GetString().ShouldBe(path);
                info.GetProperty("length").GetUInt64().ShouldBe((ulong)13);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultipleFileCalls_InOneTurn_ReturnEveryResult(bool concurrent)
    {
        var first = LlmTckToolReplay.CreateResponse("call-first", FileAccessProvider.ReadFileToolName, "{\"fileName\":\"first.txt\"}");
        var second = LlmTckToolReplay.CreateResponse("call-second", FileContextToolNames.ReadRange, "{\"path\":\"second.txt\"}");
        var missing = LlmTckToolReplay.CreateResponse("call-missing", FileContextToolNames.ReadRange, "{\"path\":\"missing.txt\"}");

        var requests = await RunToolLoopAsync($"[{first},{second},{missing}]", concurrent);

        foreach (var request in requests)
        {
            var results = LlmTckToolAssertions.AssertClosedCalls(request, "call-first", "call-second", "call-missing");
            results.Single(result => string.Equals(result.GetProperty("tool_call_id").GetString(), "call-first", StringComparison.Ordinal))
                .GetProperty("content").GetString()!.ShouldContain("first-content");
            results.Single(result => string.Equals(result.GetProperty("tool_call_id").GetString(), "call-second", StringComparison.Ordinal))
                .GetProperty("content").GetString()!.ShouldContain("second-content");
            results.Single(result => string.Equals(result.GetProperty("tool_call_id").GetString(), "call-missing", StringComparison.Ordinal))
                .GetProperty("content").GetString().ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task LiteralNullAssistantText_IsNotInterpretedAsToolReplay()
    {
        var requests = await RunToolLoopAsync(
            LlmTckToolReplay.CreateResponse("read", FileAccessProvider.ReadFileToolName, "{\"fileName\":\"first.txt\"}"),
            finalResponse: "null");

        foreach (var request in requests)
        {
            LlmTckToolAssertions.AssertClosedCalls(request, "read");
        }
    }

    [Fact]
    public async Task OperationTimeout_ReturnsToolErrorAndSurvivesSessionRestore()
    {
        var requests = await RunToolLoopAsync(
            LlmTckToolReplay.CreateResponse("timeout", FileContextToolNames.ReadRange, "{\"path\":\"slow.txt\",\"startLine\":2}"),
            operationTimeout: TimeSpan.FromMilliseconds(1));

        foreach (var request in requests)
        {
            var results = LlmTckToolAssertions.AssertClosedCalls(request, "timeout");
            results[0].GetProperty("content").GetString()!.ShouldContain("Error: Function failed.");
        }
    }

    private static async Task<string[]> RunToolLoopAsync(string replay, bool concurrent = false, string finalResponse = "Inspection completed.", TimeSpan? operationTimeout = null)
    {
        const string prompt = "Inspect the files.";
        LlmTckToolReplay.Reset();
        var host = new LlmTckTestHost(() => new LlmTckConfigurationBuilder()
            .AddModel(FileContextAgentLlmTckTests.Model, LlmTckModelKind.Chat)
            .AddChatScenario("protocol", scenario => scenario.ForModel(FileContextAgentLlmTckTests.Model)
                .WhenUserContains(prompt).Responds(replay).Responds(finalResponse).Responds("Follow-up completed."))
            .Build());
        await using var hostLifetime = host.ConfigureAwait(false);
        await host.StartAsync().ConfigureAwait(false);
        var storage = await TestStorageScope.CreateAsync().ConfigureAwait(false);
        await using var storageLifetime = storage.ConfigureAwait(false);
        var store = new ManagedCodeStorageFileStore(storage.Storage);
        await store.WriteAsync("empty.txt", string.Empty).ConfigureAwait(false);
        await store.WriteAsync("first.txt", "first-content").ConfigureAwait(false);
        await store.WriteAsync("second.txt", "second-content").ConfigureAwait(false);
        if (operationTimeout is not null)
        {
            await store.WriteAsync("slow.txt", new string('a', 1_000_000)).ConfigureAwait(false);
        }
        var options = new FileContextOptions { RequireReadToolApproval = false, OperationTimeout = operationTimeout };
        store = new ManagedCodeStorageFileStore(storage.Storage, options);
        using var provider = new FileContextProvider(store, new FileContextService(store, options), options);
        var sdk = new OpenAIClient(new ApiKeyCredential("not-required-by-llm-tck"),
            new OpenAIClientOptions { Endpoint = LlmTckToolReplay.CreateRouteUri(host.Endpoint) });
        using var client = sdk.GetChatClient(FileContextAgentLlmTckTests.Model).AsIChatClient().AsBuilder()
            .UseAIContextProviders(provider)
            .UseFunctionInvocation(configure: options => options.AllowConcurrentInvocation = concurrent).Build();
        var agent = new ChatClientAgent(client, new ChatClientAgentOptions { UseProvidedChatClientAsIs = true });

        await RunSessionRoundTripAsync(agent, prompt, finalResponse).ConfigureAwait(false);
        LlmTckToolReplay.RecordedRequests.Count.ShouldBe(3);
        var assertions = await host.GetAssertionsAsync().ConfigureAwait(false);
        assertions.Matched.ShouldBe(3);
        assertions.Unmatched.ShouldBe(0);
        return LlmTckToolReplay.RecordedRequests.Skip(1).ToArray();
    }

    private static async Task RunSessionRoundTripAsync(ChatClientAgent agent, string prompt, string finalResponse)
    {
        var session = await agent.CreateSessionAsync().ConfigureAwait(false);
        var response = await agent.RunAsync(prompt, session).ConfigureAwait(false);
        response.Text.ShouldBe(finalResponse);
        var saved = await agent.SerializeSessionAsync(session).ConfigureAwait(false);
        var restored = await agent.DeserializeSessionAsync(saved).ConfigureAwait(false);
        var followUp = await agent.RunAsync("Continue the inspection.", restored).ConfigureAwait(false);
        followUp.Text.ShouldBe("Follow-up completed.");
    }
}
