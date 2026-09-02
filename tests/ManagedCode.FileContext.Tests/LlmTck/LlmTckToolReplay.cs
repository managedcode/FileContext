using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using ManagedCode.LlmTck.OpenAI;
using ManagedCode.LlmTck.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ManagedCode.FileContext.Tests.LlmTck;

internal static class LlmTckToolReplay
{
    private const string RoutePrefix = "/tool-replay/openai/v1";
    private const string ReplayKind = "tool_call";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentQueue<string> Requests = new();

    public static IReadOnlyList<string> RecordedRequests => Requests.ToArray();

    public static void Reset() => Requests.Clear();

    public static Uri CreateRouteUri(Uri endpoint)
    {
        var root = endpoint.AbsoluteUri.EndsWith('/') ? endpoint : new Uri($"{endpoint}/");
        return new Uri(root, $"{RoutePrefix.TrimStart('/')}/");
    }

    public static string CreateResponse(string callId, string toolName, string argumentsJson)
    {
        using var arguments = JsonDocument.Parse(argumentsJson);
        return JsonSerializer.Serialize(new
        {
            llmTckReplayKind = ReplayKind,
            callId,
            toolName,
            arguments = arguments.RootElement,
        }, JsonOptions);
    }

    public static IEndpointRouteBuilder MapLlmTckToolReplay(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost($"{RoutePrefix}/chat/completions", CompleteChatAsync);
        return endpoints;
    }

    private static async Task<IResult> CompleteChatAsync(
        HttpContext context,
        ILlmTckRuntime runtime,
        CancellationToken cancellationToken)
    {
        context.Request.EnableBuffering();
        using (var reader = new StreamReader(context.Request.Body, leaveOpen: true))
        {
            Requests.Enqueue(await reader.ReadToEndAsync(cancellationToken));
            context.Request.Body.Position = 0;
        }

        var request = await context.Request.ReadFromJsonAsync<OpenAiChatCompletionRequest>(JsonOptions, cancellationToken);
        if (request is null)
        {
            return Results.BadRequest();
        }

        var result = await runtime.CompleteChatAsync(
            OpenAiWireMapper.ToRuntimeRequest(request),
            cancellationToken: cancellationToken);
        if (!result.IsSuccess)
        {
            return Results.Json(OpenAiWireMapper.ToError(result.ErrorCode!, result.ErrorMessage!), statusCode: result.StatusCode);
        }

        return TryReadEnvelope(result.Content, out var envelope)
            ? Results.Json(CreateCompletion(envelope))
            : Results.Json(OpenAiWireMapper.ToChatResponse(result));
    }

    private static bool TryReadEnvelope(string content, out ToolCallEnvelope envelope)
    {
        envelope = default!;
        try
        {
            var payload = JsonSerializer.Deserialize<ToolCallPayload>(content, JsonOptions);
            if (payload?.Kind != ReplayKind || string.IsNullOrWhiteSpace(payload.CallId) || string.IsNullOrWhiteSpace(payload.ToolName))
            {
                return false;
            }

            envelope = new ToolCallEnvelope(payload.CallId, payload.ToolName, payload.Arguments.GetRawText());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static object CreateCompletion(ToolCallEnvelope envelope) => new
    {
        id = $"chatcmpl-{Guid.NewGuid():N}",
        @object = "chat.completion",
        created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        model = FileContextAgentLlmTckTests.Model,
        choices = new[]
        {
            new
            {
                index = 0,
                message = new
                {
                    role = "assistant",
                    content = (string?)null,
                    tool_calls = new[]
                    {
                        new
                        {
                            id = envelope.CallId,
                            type = "function",
                            function = new { name = envelope.ToolName, arguments = envelope.ArgumentsJson },
                        },
                    },
                },
                finish_reason = "tool_calls",
            },
        },
    };

    private sealed record ToolCallEnvelope(string CallId, string ToolName, string ArgumentsJson);

    private sealed record ToolCallPayload(
        [property: JsonPropertyName("llmTckReplayKind")] string? Kind,
        string? CallId,
        string? ToolName,
        JsonElement Arguments);
}
