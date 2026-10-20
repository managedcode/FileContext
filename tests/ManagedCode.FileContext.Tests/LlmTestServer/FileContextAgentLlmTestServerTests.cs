using System.ClientModel;
using ManagedCode.LlmTestServer.Configuration;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.Storage.Core.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

namespace ManagedCode.FileContext.Tests.LlmTestServer;

public sealed class FileContextAgentLlmTestServerTests
{
    internal const string Model = "gpt-4.1-mini";

    [Fact]
    public async Task Agent_reads_docx_through_native_tool_and_receives_bounded_text()
    {
        const string prompt = "Read family.docx.";
        LlmTestServerToolReplay.Reset();
        await using var host = new LlmTestServerTestHost(() => new LlmTestServerConfigurationBuilder()
            .AddModel(Model, LlmTestServerModelKind.Chat)
            .AddChatScenario("docx-tool-loop", scenario => scenario
                .ForModel(Model)
                .WhenUserContains(prompt)
                .Responds(LlmTestServerToolReplay.CreateResponse("docx-1", FileContextToolNames.DocxText,
                    "{\"path\":\"family.docx\",\"paragraphCount\":1}"))
                .Responds("Document read"))
            .Build());
        await host.StartAsync();
        await using var storage = await TestStorageScope.CreateAsync();
        var upload = await storage.Storage.UploadAsync(FileContextDocxTestFiles.WithParagraphs(
            "Grandmother's family history", "Next paragraph"),
            new UploadOptions { FileName = "family.docx" });
        upload.IsSuccess.ShouldBeTrue(upload.Problem?.Detail);
        var store = new ManagedCodeStorageFileStore(storage.Storage);
        using var provider = new FileContextProvider(store, new FileContextService(store),
            new FileContextOptions { RequireReadToolApproval = false });
        var sdk = new OpenAIClient(new ApiKeyCredential("not-required-by-llm-test-server"),
            new OpenAIClientOptions { Endpoint = LlmTestServerToolReplay.CreateRouteUri(host.Endpoint) });
        using var client = sdk.GetChatClient(Model).AsIChatClient().AsBuilder()
            .UseAIContextProviders(provider).UseFunctionInvocation().Build();
        var agent = new ChatClientAgent(client, new ChatClientAgentOptions { UseProvidedChatClientAsIs = true });

        (await agent.RunAsync(prompt)).Text.ShouldBe("Document read");
        LlmTestServerToolReplay.RecordedRequests.Count.ShouldBe(2);
        LlmTestServerToolReplay.RecordedRequests[0].ShouldContain(FileContextToolNames.DocxText);
        LlmTestServerToolReplay.RecordedRequests[1].ShouldContain("Grandmother's family history");
        LlmTestServerToolReplay.RecordedRequests[1].ShouldContain("nextParagraph");
        (await host.GetAssertionsAsync()).Matched.ShouldBe(2);
    }

    [Fact]
    public async Task Agent_WhenLlmRequestsStandardReadTool_ExecutesToolAndReturnsGroundedAnswer()
    {
        const string prompt = "Read the second line of notes.txt.";
        const string expected = "The second line is storage-grounded.";
        LlmTestServerToolReplay.Reset();
        await using var host = new LlmTestServerTestHost(() => new LlmTestServerConfigurationBuilder()
            .AddModel(Model, LlmTestServerModelKind.Chat)
            .AddChatScenario("file-context-tool-loop", scenario => scenario
                .ForModel(Model)
                .WhenUserContains(prompt)
                .Responds(LlmTestServerToolReplay.CreateResponse(
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
            new ApiKeyCredential("not-required-by-llm-test-server"),
            new OpenAIClientOptions { Endpoint = LlmTestServerToolReplay.CreateRouteUri(host.Endpoint) });
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

    private static async Task AssertDefaultToolCatalogAsync(LlmTestServerTestHost host)
    {
        LlmTestServerToolReplay.RecordedRequests.Count.ShouldBe(2);
        LlmTestServerToolReplay.RecordedRequests[0].ShouldContain(FileAccessProvider.ReadFileToolName);
        LlmTestServerToolReplay.RecordedRequests[0].ShouldContain(FileAccessProvider.LsToolName);
        LlmTestServerToolReplay.RecordedRequests[0].ShouldContain(FileAccessProvider.GrepToolName);
        LlmTestServerToolReplay.RecordedRequests[0].ShouldContain(FileContextToolNames.ReadRange);
        LlmTestServerToolReplay.RecordedRequests[0].ShouldContain(FileContextToolNames.GetInfo);
        LlmTestServerToolReplay.RecordedRequests[0].ShouldContain(FileContextToolNames.SearchMarkdownGraph);
        LlmTestServerToolReplay.RecordedRequests[0].ShouldContain(FileContextToolNames.ExportMarkdownGraph);
        LlmTestServerToolReplay.RecordedRequests[0].ShouldNotContain(FileAccessProvider.WriteToolName);
        LlmTestServerToolReplay.RecordedRequests[1].ShouldContain("storage-grounded");
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
        LlmTestServerToolReplay.Reset();
        await using var host = new LlmTestServerTestHost(() => new LlmTestServerConfigurationBuilder()
            .AddModel(Model, LlmTestServerModelKind.Chat)
            .AddChatScenario($"extended-{toolName}", scenario => scenario
                .ForModel(Model)
                .WhenUserContains(prompt)
                .Responds(LlmTestServerToolReplay.CreateResponse($"call-{toolName}", toolName, arguments))
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
            new ApiKeyCredential("not-required-by-llm-test-server"),
            new OpenAIClientOptions { Endpoint = LlmTestServerToolReplay.CreateRouteUri(host.Endpoint) });
        using var chatClient = sdk.GetChatClient(Model)
            .AsIChatClient()
            .AsBuilder()
            .UseAIContextProviders(contextProvider)
            .UseFunctionInvocation()
            .Build();
        var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions { UseProvidedChatClientAsIs = true });

        var response = await agent.RunAsync(prompt);

        response.Text.ShouldBe(expected);
        LlmTestServerToolReplay.RecordedRequests.Count.ShouldBe(2);
        LlmTestServerToolReplay.RecordedRequests[1].ShouldContain(resultMarker, Case.Insensitive);
        var assertions = await host.GetAssertionsAsync();
        assertions.Matched.ShouldBe(2);
        assertions.Unmatched.ShouldBe(0);
        assertions.ScenarioExhausted.ShouldBe(0);
    }
}
