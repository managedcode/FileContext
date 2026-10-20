using System.ClientModel;
using ManagedCode.LlmTestServer.Configuration;
using ManagedCode.LlmTestServer.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

namespace ManagedCode.FileContext.Tests.LlmTestServer;

public sealed class FileAccessMutationLlmTestServerTests
{
    private const string FileName = "notes.txt";
    private const string ExpectedAgentResponse = "The requested file mutation completed.";

    [Theory]
    [InlineData(FileAccessProvider.WriteToolName, "{\"fileName\":\"notes.txt\",\"content\":\"created\",\"overwrite\":false}", null, "created")]
    [InlineData(FileAccessProvider.DeleteFileToolName, "{\"fileName\":\"notes.txt\"}", "before", null)]
    [InlineData(FileAccessProvider.ReplaceToolName, "{\"fileName\":\"notes.txt\",\"oldString\":\"before\",\"newString\":\"after\",\"replaceAll\":false}", "before", "after")]
    [InlineData(FileAccessProvider.ReplaceLinesToolName, "{\"fileName\":\"notes.txt\",\"edits\":[{\"line_number\":2,\"new_line\":\"changed\\n\"}]}", "one\ntwo\nthree", "one\nchanged\nthree")]
    public async Task Agent_WhenLlmRequestsMutationTool_ChangesRealFileSystemContent(
        string toolName,
        string arguments,
        string? initialContent,
        string? expectedContent)
    {
        var prompt = $"Invoke {toolName}.";
        LlmTestServerToolReplay.Reset();
        await using var host = new LlmTestServerTestHost(() => CreateConfiguration(prompt, toolName, arguments));
        await host.StartAsync();
        await using var storage = await TestStorageScope.CreateAsync();
        var store = new ManagedCodeStorageFileStore(storage.Storage);
        if (initialContent is not null)
        {
            await store.WriteAsync(FileName, initialContent);
        }

        using var contextProvider = new FileContextProvider(
            store,
            new FileContextService(store),
            new FileContextOptions
            {
                EnableWriteTools = true,
                RequireReadToolApproval = false,
                RequireWriteToolApproval = false,
            });
        var sdk = new OpenAIClient(
            new ApiKeyCredential("not-required-by-llm-test-server"),
            new OpenAIClientOptions { Endpoint = LlmTestServerToolReplay.CreateRouteUri(host.Endpoint) });
        using var chatClient = sdk.GetChatClient(FileContextAgentLlmTestServerTests.Model)
            .AsIChatClient()
            .AsBuilder()
            .UseAIContextProviders(contextProvider)
            .UseFunctionInvocation()
            .Build();
        var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions { UseProvidedChatClientAsIs = true });

        var response = await agent.RunAsync(prompt);

        response.Text.ShouldBe(ExpectedAgentResponse);
        (await store.ReadAsync(FileName)).ShouldBe(expectedContent);
        LlmTestServerToolReplay.RecordedRequests.Count.ShouldBe(2);
        LlmTestServerToolReplay.RecordedRequests[0].ShouldContain(toolName);
        var results = LlmTestServerToolAssertions.AssertClosedCalls(
            LlmTestServerToolReplay.RecordedRequests[1], $"call-{toolName}");
        results[0].GetProperty("content").GetString().ShouldNotBeNullOrWhiteSpace();
        var assertions = await host.GetAssertionsAsync();
        assertions.Matched.ShouldBe(2);
        assertions.Unmatched.ShouldBe(0);
        assertions.ScenarioExhausted.ShouldBe(0);
    }

    private static LlmTestServerConfiguration CreateConfiguration(string prompt, string toolName, string arguments)
    {
        return new LlmTestServerConfigurationBuilder()
            .AddModel(FileContextAgentLlmTestServerTests.Model, LlmTestServerModelKind.Chat)
            .AddChatScenario($"mutation-{toolName}", scenario => scenario
                .ForModel(FileContextAgentLlmTestServerTests.Model)
                .WhenUserContains(prompt)
                .Responds(LlmTestServerToolReplay.CreateResponse($"call-{toolName}", toolName, arguments))
                .Responds(ExpectedAgentResponse))
            .Build();
    }
}
