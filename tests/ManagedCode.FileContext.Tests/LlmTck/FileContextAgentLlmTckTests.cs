using System.ClientModel;
using ManagedCode.LlmTck.Configuration;
using ManagedCode.LlmTck.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

namespace ManagedCode.FileContext.Tests.LlmTck;

public sealed class FileContextAgentLlmTckTests
{
    internal const string Model = "gpt-4.1-mini";

    [Fact]
    public async Task Agent_WhenLlmRequestsStandardReadTool_ExecutesToolAndReturnsGroundedAnswer()
    {
        const string prompt = "Read the second line of notes.txt.";
        const string expected = "The second line is storage-grounded.";
        LlmTckToolReplay.Reset();
        await using var host = new LlmTckTestHost(() => new LlmTckConfigurationBuilder()
            .AddModel(Model, LlmTckModelKind.Chat)
            .AddChatScenario("file-context-tool-loop", scenario => scenario
                .ForModel(Model)
                .WhenUserContains(prompt)
                .Responds(LlmTckToolReplay.CreateResponse(
                    "read-range-1",
                    FileAccessProvider.ReadFileToolName,
                    "{\"fileName\":\"notes.txt\"}"))
                .Responds(expected))
            .Build());
        await host.StartAsync();
        await using var storage = await TestStorageScope.CreateAsync();
        var store = new ManagedCodeStorageFileStore(storage.Storage);
        await store.WriteAsync("notes.txt", "first\nstorage-grounded\nthird");
        using var contextProvider = new FileContextProvider(
            store,
            new FileContextService(store),
            new FileContextOptions { RequireReadToolApproval = false });
        var sdk = new OpenAIClient(
            new ApiKeyCredential("not-required-by-llm-tck"),
            new OpenAIClientOptions { Endpoint = LlmTckToolReplay.CreateRouteUri(host.Endpoint) });
        using var chatClient = sdk.GetChatClient(Model)
            .AsIChatClient()
            .AsBuilder()
            .UseAIContextProviders(contextProvider)
            .UseFunctionInvocation()
            .Build();
        var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions { UseProvidedChatClientAsIs = true });

        var response = await agent.RunAsync(prompt);

        response.Text.ShouldBe(expected);
        await AssertDefaultToolCatalogAsync(host);
    }

    private static async Task AssertDefaultToolCatalogAsync(LlmTckTestHost host)
    {
        LlmTckToolReplay.RecordedRequests.Count.ShouldBe(2);
        LlmTckToolReplay.RecordedRequests[0].ShouldContain(FileAccessProvider.ReadFileToolName);
        LlmTckToolReplay.RecordedRequests[0].ShouldContain(FileAccessProvider.LsToolName);
        LlmTckToolReplay.RecordedRequests[0].ShouldContain(FileAccessProvider.GrepToolName);
        LlmTckToolReplay.RecordedRequests[0].ShouldContain(FileContextToolNames.ReadRange);
        LlmTckToolReplay.RecordedRequests[0].ShouldContain(FileContextToolNames.GetInfo);
        LlmTckToolReplay.RecordedRequests[0].ShouldContain(FileContextToolNames.SearchMarkdownGraph);
        LlmTckToolReplay.RecordedRequests[0].ShouldContain(FileContextToolNames.ExportMarkdownGraph);
        LlmTckToolReplay.RecordedRequests[0].ShouldNotContain(FileAccessProvider.WriteToolName);
        LlmTckToolReplay.RecordedRequests[1].ShouldContain("storage-grounded");
        var assertions = await host.GetAssertionsAsync().ConfigureAwait(false);
        assertions.Matched.ShouldBe(2);
        assertions.Unmatched.ShouldBe(0);
        assertions.ScenarioExhausted.ShouldBe(0);
    }

    [Theory]
    [InlineData(FileAccessProvider.LsToolName, "{\"directory\":\"\",\"globPattern\":\"*.txt\"}", "notes.txt")]
    [InlineData(FileAccessProvider.GrepToolName, "{\"regexPattern\":\"storage-grounded\",\"globPattern\":\"*.txt\",\"directory\":\"\"}", "storage-grounded")]
    [InlineData(FileContextToolNames.ReadRange, "{\"path\":\"notes.txt\",\"startLine\":2,\"lineCount\":1}", "storage-grounded")]
    [InlineData(FileContextToolNames.GetInfo, "{\"path\":\"notes.txt\"}", "notes.txt")]
    [InlineData(FileContextToolNames.SearchMarkdownGraph, "{\"query\":\"Agent Context\",\"directory\":\"docs\"}", "Agent Context")]
    [InlineData(FileContextToolNames.ExportMarkdownGraph, "{\"format\":\"Mermaid\",\"directory\":\"docs\"}", "graph LR")]
    public async Task Agent_WhenLlmRequestsReadOnlyTool_ExecutesAgainstRealFileSystem(
        string toolName,
        string arguments,
        string resultMarker)
    {
        var prompt = $"Invoke {toolName}.";
        const string expected = "The requested file-context tool completed.";
        LlmTckToolReplay.Reset();
        await using var host = new LlmTckTestHost(() => new LlmTckConfigurationBuilder()
            .AddModel(Model, LlmTckModelKind.Chat)
            .AddChatScenario($"extended-{toolName}", scenario => scenario
                .ForModel(Model)
                .WhenUserContains(prompt)
                .Responds(LlmTckToolReplay.CreateResponse($"call-{toolName}", toolName, arguments))
                .Responds(expected))
            .Build());
        await host.StartAsync();
        await using var storage = await TestStorageScope.CreateAsync();
        var store = new ManagedCodeStorageFileStore(storage.Storage);
        await store.WriteAsync("notes.txt", "first\nstorage-grounded\nthird");
        await store.WriteAsync("docs/agent.md", "# Agent Context\n\nAgents inspect storage-backed files.");
        using var contextProvider = new FileContextProvider(
            store,
            new FileContextService(store),
            new FileContextOptions { RequireReadToolApproval = false });
        var sdk = new OpenAIClient(
            new ApiKeyCredential("not-required-by-llm-tck"),
            new OpenAIClientOptions { Endpoint = LlmTckToolReplay.CreateRouteUri(host.Endpoint) });
        using var chatClient = sdk.GetChatClient(Model)
            .AsIChatClient()
            .AsBuilder()
            .UseAIContextProviders(contextProvider)
            .UseFunctionInvocation()
            .Build();
        var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions { UseProvidedChatClientAsIs = true });

        var response = await agent.RunAsync(prompt);

        response.Text.ShouldBe(expected);
        LlmTckToolReplay.RecordedRequests.Count.ShouldBe(2);
        LlmTckToolReplay.RecordedRequests[1].ShouldContain(resultMarker, Case.Insensitive);
        var assertions = await host.GetAssertionsAsync();
        assertions.Matched.ShouldBe(2);
        assertions.Unmatched.ShouldBe(0);
        assertions.ScenarioExhausted.ShouldBe(0);
    }
}
