using System.ClientModel;
using System.Text.Json;
using ManagedCode.LlmTck.Configuration;
using ManagedCode.LlmTck.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

namespace ManagedCode.FileContext.Tests.LlmTck;

public sealed class FileDocumentCreationLlmTckTests
{
    private const string Response = "Created the requested document.";

    [Theory]
    [InlineData(FileContextToolNames.CreateCsv, "{\"fileName\":\"report.csv\",\"document\":{\"rows\":[[\"Name\",\"Count\"],[\"A\",\"2\"]]}}", "text/csv")]
    [InlineData(FileContextToolNames.CreateWorkbook, "{\"fileName\":\"report.xlsx\",\"workbook\":{\"sheets\":[{\"name\":\"Sales\",\"rows\":[[{\"text\":\"A\"},{\"number\":2}]]}]}}", "application/vnd.openxmlformats")]
    [InlineData(FileContextToolNames.CreatePdf, "{\"fileName\":\"report.pdf\",\"document\":{\"title\":\"Report\",\"paragraphs\":[\"Sales report\"]}}", "application/pdf")]
    public async Task Agent_creates_a_real_document_and_receives_a_closed_tool_result(string toolName, string arguments, string mediaPrefix)
    {
        var prompt = $"Create with {toolName}.";
        LlmTckToolReplay.Reset();
        await using var host = new LlmTckTestHost(() => new LlmTckConfigurationBuilder()
            .AddModel(FileContextAgentLlmTckTests.Model, LlmTckModelKind.Chat)
            .AddChatScenario($"document-{toolName}", scenario => scenario.ForModel(FileContextAgentLlmTckTests.Model)
                .WhenUserContains(prompt).Responds(LlmTckToolReplay.CreateResponse($"call-{toolName}", toolName, arguments))
                .Responds(Response)).Build());
        await host.StartAsync();
        await using var storage = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { RootPrefix = "conversation", EnableWriteTools = true, RequireReadToolApproval = false, RequireWriteToolApproval = false };
        var store = new ManagedCodeStorageFileStore(storage.Storage, options);
        var context = new FileContextService(store, options);
        using var provider = new FileContextProvider(store, context, options);
        var sdk = new OpenAIClient(new ApiKeyCredential("not-required-by-llm-tck"),
            new OpenAIClientOptions { Endpoint = LlmTckToolReplay.CreateRouteUri(host.Endpoint) });
        using var client = sdk.GetChatClient(FileContextAgentLlmTckTests.Model).AsIChatClient().AsBuilder()
            .UseAIContextProviders(provider).UseFunctionInvocation().Build();
        var agent = new ChatClientAgent(client, new ChatClientAgentOptions { UseProvidedChatClientAsIs = true });
        (await agent.RunAsync(prompt)).Text.ShouldBe(Response);
        var results = LlmTckToolAssertions.AssertClosedCalls(LlmTckToolReplay.RecordedRequests[1], $"call-{toolName}");
        var created = JsonSerializer.Deserialize<FileContextCreatedFile>(results[0].GetProperty("content").GetString()!, JsonSerializerOptions.Web)!;
        created.MediaType.ShouldStartWith(mediaPrefix);
        (await context.GetInfoAsync(created.Path))!.Length.ShouldBe((ulong)created.Length);
        created.Length.ShouldBeGreaterThan(0);
        (await context.ListFilesAsync()).Single().Path.ShouldBe(created.Path);
        var assertions = await host.GetAssertionsAsync();
        assertions.Matched.ShouldBe(2);
        assertions.Unmatched.ShouldBe(0);
    }
}
