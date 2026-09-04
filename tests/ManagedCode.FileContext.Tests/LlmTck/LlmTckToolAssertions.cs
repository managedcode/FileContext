using System.Text.Json;

namespace ManagedCode.FileContext.Tests.LlmTck;

internal static class LlmTckToolAssertions
{
    public static JsonElement[] AssertClosedCalls(string request, params string[] expectedCallIds)
    {
        using var document = JsonDocument.Parse(request);
        var pending = new HashSet<string>(StringComparer.Ordinal);
        var calls = new List<string>();
        var results = new List<JsonElement>();
        foreach (var message in document.RootElement.GetProperty("messages").EnumerateArray())
        {
            if (string.Equals(message.GetProperty("role").GetString(), "tool", StringComparison.Ordinal))
            {
                var id = message.GetProperty("tool_call_id").GetString()!;
                pending.Remove(id).ShouldBeTrue($"Unexpected or duplicate tool result: {id}");
                results.Add(message.Clone());
                continue;
            }

            pending.ShouldBeEmpty("All tool calls must be answered before another user/assistant message.");
            if (message.TryGetProperty("tool_calls", out var toolCalls))
            {
                message.GetProperty("role").GetString().ShouldBe("assistant");
                foreach (var call in toolCalls.EnumerateArray())
                {
                    var id = call.GetProperty("id").GetString()!;
                    pending.Add(id).ShouldBeTrue($"Duplicate tool call: {id}");
                    calls.Add(id);
                }
            }
        }

        pending.ShouldBeEmpty("No tool call may be left without a result.");
        calls.Order(StringComparer.Ordinal).ShouldBe(expectedCallIds.Order(StringComparer.Ordinal));
        results.Select(result => result.GetProperty("tool_call_id").GetString())
            .Order(StringComparer.Ordinal).ShouldBe(expectedCallIds.Order(StringComparer.Ordinal));
        return results.ToArray();
    }
}
