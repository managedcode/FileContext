using System.ClientModel;
using ManagedCode.LlmTck.Configuration;
using ManagedCode.LlmTck.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

namespace ManagedCode.FileContext.Tests.LlmTck;

public sealed class FileAccessMutationLlmTckTests
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
        LlmTckToolReplay.Reset();
        await using var host = new LlmTckTestHost(() => CreateConfiguration(prompt, toolName, arguments));
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
            new ApiKeyCredential("not-required-by-llm-tck"),
            new OpenAIClientOptions { Endpoint = LlmTckToolReplay.CreateRouteUri(host.Endpoint) });
        using var chatClient = sdk.GetChatClient(FileContextAgentLlmTckTests.Model)
            .AsIChatClient()
            .AsBuilder()
            .UseAIContextProviders(contextProvider)
            .UseFunctionInvocation()
            .Build();
        var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions { UseProvidedChatClientAsIs = true });

        var response = await agent.RunAsync(prompt);

        response.Text.ShouldBe(ExpectedAgentResponse);
        (await store.ReadAsync(FileName)).ShouldBe(expectedContent);
        LlmTckToolReplay.RecordedRequests.Count.ShouldBe(2);
        LlmTckToolReplay.RecordedRequests[0].ShouldContain(toolName);
        var assertions = await host.GetAssertionsAsync();
        assertions.Matched.ShouldBe(2);
        assertions.Unmatched.ShouldBe(0);
        assertions.ScenarioExhausted.ShouldBe(0);
    }

    private static LlmTckConfiguration CreateConfiguration(string prompt, string toolName, string arguments)
    {
        return new LlmTckConfigurationBuilder()
            .AddModel(FileContextAgentLlmTckTests.Model, LlmTckModelKind.Chat)
            .AddChatScenario($"mutation-{toolName}", scenario => scenario
                .ForModel(FileContextAgentLlmTckTests.Model)
                .WhenUserContains(prompt)
                .Responds(LlmTckToolReplay.CreateResponse($"call-{toolName}", toolName, arguments))
                .Responds(ExpectedAgentResponse))
            .Build();
    }
}
