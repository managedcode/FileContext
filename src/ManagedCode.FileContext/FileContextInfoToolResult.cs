using System.Text.Json.Serialization;

namespace ManagedCode.FileContext;

internal sealed record FileContextInfoToolResult(
    string Status,
    string Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FileContextInfo? Info);
