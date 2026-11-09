using System.Text.Json;
using Microsoft.Agents.AI;
using static ManagedCode.FileContext.Tests.LlmTestServer.FileToolResultProtocolFixture;

namespace ManagedCode.FileContext.Tests.LlmTestServer;

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
        var requests = await RunToolLoopAsync(LlmTestServerToolReplay.CreateResponse("call-empty", toolName, arguments));

        foreach (var request in requests)
        {
            var results = LlmTestServerToolAssertions.AssertClosedCalls(request, "call-empty");
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
        var requests = await RunToolLoopAsync(LlmTestServerToolReplay.CreateResponse(
            "metadata", FileContextToolNames.GetInfo, JsonSerializer.Serialize(new { path })));

        foreach (var request in requests)
        {
            var results = LlmTestServerToolAssertions.AssertClosedCalls(request, "metadata");
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

    [Fact]
    public async Task ReadingGuidance_ReachesModelAndRestoredSession()
    {
        var requests = await RunToolLoopAsync(LlmTestServerToolReplay.CreateResponse(
            "metadata", FileContextToolNames.GetInfo, "{\"path\":\"first.txt\"}"));

        foreach (var request in requests)
        {
            using var document = JsonDocument.Parse(request);
            var instructions = string.Join("\n", document.RootElement.GetProperty("messages").EnumerateArray()
                .Where(message => message.GetProperty("role").GetString() is "system" or "developer")
                .Select(message => message.GetProperty("content").ToString()));
            instructions.ShouldContain("length in bytes");
            instructions.ShouldContain("Do not read an entire large file into model context by default");
            instructions.ShouldContain(FileAccessProvider.GrepToolName);
            instructions.ShouldContain(FileContextToolNames.ReadRange);
            instructions.ShouldContain("do not silently omit remaining content");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultipleFileCalls_InOneTurn_ReturnEveryResult(bool concurrent)
    {
        var first = LlmTestServerToolReplay.CreateResponse("call-first", FileAccessProvider.ReadFileToolName, "{\"fileName\":\"first.txt\"}");
        var second = LlmTestServerToolReplay.CreateResponse("call-second", FileContextToolNames.ReadRange, "{\"path\":\"second.txt\"}");
        var missing = LlmTestServerToolReplay.CreateResponse("call-missing", FileContextToolNames.ReadRange, "{\"path\":\"missing.txt\"}");

        var requests = await RunToolLoopAsync($"[{first},{second},{missing}]", concurrent);

        foreach (var request in requests)
        {
            var results = LlmTestServerToolAssertions.AssertClosedCalls(request, "call-first", "call-second", "call-missing");
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
            LlmTestServerToolReplay.CreateResponse("read", FileAccessProvider.ReadFileToolName, "{\"fileName\":\"first.txt\"}"),
            finalResponse: "null");

        foreach (var request in requests)
        {
            LlmTestServerToolAssertions.AssertClosedCalls(request, "read");
        }
    }

    [Fact]
    public async Task OperationTimeout_ReturnsToolErrorAndSurvivesSessionRestore()
    {
        var requests = await RunToolLoopAsync(
            LlmTestServerToolReplay.CreateResponse("timeout", FileContextToolNames.ReadRange, "{\"path\":\"slow.txt\",\"startLine\":2}"),
            operationTimeout: TimeSpan.FromMilliseconds(10));

        foreach (var request in requests)
        {
            var results = LlmTestServerToolAssertions.AssertClosedCalls(request, "timeout");
            results[0].GetProperty("content").GetString()!.ShouldContain("Error: Function failed.");
        }
    }

    [Fact]
    public async Task Restored_session_keeps_docx_cursor_and_closes_new_docx_and_csv_calls()
    {
        var first = LlmTestServerToolReplay.CreateResponse("docx-first", FileContextToolNames.DocxText, """{"path":"chapter.docx"}""");
        var next = LlmTestServerToolReplay.CreateResponse("docx-next", FileContextToolNames.DocxText,
            """{"path":"chapter.docx","startParagraph":1,"startCharacter":4}""");
        var csv = LlmTestServerToolReplay.CreateResponse("csv", FileContextToolNames.TablesInfo, """{"path":"ledger.csv"}""");

        var requests = await RunToolLoopAsync(first, officeFollowUp: $"[{next},{csv}]");

        var firstResult = LlmTestServerToolAssertions.AssertClosedCalls(requests[0], "docx-first").Single();
        var restoredInput = LlmTestServerToolAssertions.AssertClosedCalls(requests[1], "docx-first").Single();
        restoredInput.GetProperty("content").GetString().ShouldBe(firstResult.GetProperty("content").GetString());
        using var initial = JsonDocument.Parse(firstResult.GetProperty("content").GetString()!);
        initial.RootElement.GetProperty("nextCharacter").GetInt32().ShouldBe(4);
        initial.RootElement.GetProperty("paragraphs")[0].GetProperty("text").GetString().ShouldBe("ABCD");

        var results = LlmTestServerToolAssertions.AssertClosedCalls(requests[2], "docx-first", "docx-next", "csv");
        using var continuation = ParseToolResult(results, "docx-next");
        continuation.RootElement.GetProperty("paragraphs")[0].GetProperty("text").GetString().ShouldBe("🧪尾");
        using var metadata = ParseToolResult(results, "csv");
        var table = metadata.RootElement.GetProperty("tables")[0];
        table.GetProperty("headers").EnumerateArray().Select(header => header.GetString()).ShouldBe(["Label", "Count"]);
        table.GetProperty("rowCount").GetInt32().ShouldBe(2);
    }

    private static JsonDocument ParseToolResult(IEnumerable<JsonElement> results, string id) =>
        JsonDocument.Parse(results.Single(result => string.Equals(result.GetProperty("tool_call_id").GetString(), id, StringComparison.Ordinal))
            .GetProperty("content").GetString()!);
}
