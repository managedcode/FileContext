using System.Text.Json;
using System.Text.Json.Serialization;

namespace ManagedCode.FileContext;

/// <summary>A report block. Content follows the block JSON schema.</summary>
public sealed record FileContextReportBlock(string Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] JsonElement Content = default);
