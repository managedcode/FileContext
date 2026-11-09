using System.ClientModel;
using ManagedCode.LlmTestServer.Configuration;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.Storage.Core.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

namespace ManagedCode.FileContext.Tests.LlmTestServer;

internal static class FileToolResultProtocolFixture
{
    internal static async Task<string[]> RunToolLoopAsync(string replay, bool concurrent = false, string finalResponse = "Inspection completed.", TimeSpan? operationTimeout = null, string? officeFollowUp = null)
    {
        const string prompt = "Inspect the files.";
        LlmTestServerToolReplay.Reset();
        var host = new LlmTestServerTestHost(() => new LlmTestServerConfigurationBuilder()
            .AddModel(FileContextAgentLlmTestServerTests.Model, LlmTestServerModelKind.Chat)
            .AddChatScenario("protocol", scenario =>
            {
                var responses = scenario.ForModel(FileContextAgentLlmTestServerTests.Model)
                    .WhenUserContains(prompt).Responds(replay).Responds(finalResponse);
                if (officeFollowUp is not null) { responses.Responds(officeFollowUp); }
                responses.Responds("Follow-up completed.");
            })
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
            storage.CreateSparseFile("slow.txt", 1L << 30);
        }
        if (officeFollowUp is not null) { await PrepareOfficeDocumentsAsync(storage, store).ConfigureAwait(false); }
        var options = new FileContextOptions
        {
            RequireReadToolApproval = false,
            OperationTimeout = operationTimeout,
            MaximumDocxTextCharacters = officeFollowUp is null ? FileContextDefaults.MaximumDocxTextCharacters : 4
        };
        store = new ManagedCodeStorageFileStore(storage.Storage, options);
        using var provider = new FileContextProvider(store, new FileContextService(store, options), options);
        var sdk = new OpenAIClient(new ApiKeyCredential("not-required-by-llm-test-server"),
            new OpenAIClientOptions { Endpoint = LlmTestServerToolReplay.CreateRouteUri(host.Endpoint) });
        using var client = sdk.GetChatClient(FileContextAgentLlmTestServerTests.Model).AsIChatClient().AsBuilder()
            .UseAIContextProviders(provider)
            .UseFunctionInvocation(configure: options => options.AllowConcurrentInvocation = concurrent).Build();
        var agent = new ChatClientAgent(client, new ChatClientAgentOptions { UseProvidedChatClientAsIs = true });

        await RunSessionRoundTripAsync(agent, prompt, finalResponse).ConfigureAwait(false);
        var requestCount = officeFollowUp is null ? 3 : 4;
        LlmTestServerToolReplay.RecordedRequests.Count.ShouldBe(requestCount);
        var assertions = await host.GetAssertionsAsync().ConfigureAwait(false);
        assertions.Matched.ShouldBe(requestCount);
        assertions.Unmatched.ShouldBe(0);
        return LlmTestServerToolReplay.RecordedRequests.Skip(1).ToArray();
    }

    private static async Task PrepareOfficeDocumentsAsync(TestStorageScope storage, ManagedCodeStorageFileStore store)
    {
        var docx = await storage.Storage.UploadAsync(FileContextDocxTestFiles.WithParagraphs("ABCD🧪尾"),
            new UploadOptions("chapter.docx")).ConfigureAwait(false);
        docx.IsSuccess.ShouldBeTrue(docx.Problem?.Detail);
        await store.WriteAsync("ledger.csv", "Label,Count\r\nFirst,1\r\nSecond,2").ConfigureAwait(false);
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
