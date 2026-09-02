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
        var assertions = await host.GetAssertionsAsync();
        assertions.Matched.ShouldBe(2);
        assertions.Unmatched.ShouldBe(0);
        assertions.ScenarioExhausted.ShouldBe(0);
    }
}
