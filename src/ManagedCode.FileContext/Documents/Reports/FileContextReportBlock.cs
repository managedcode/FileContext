using System.Text.Json;
using System.Text.Json.Serialization;

namespace ManagedCode.FileContext;

/// <summary>A report block. Content follows the block JSON schema.</summary>
public sealed record FileContextReportBlock(string Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] JsonElement Content)
{
    /// <summary>Creates a report block with no content.</summary>
    // Bind Content through its init property so schema export can represent omitted content.
    [JsonConstructor]
    public FileContextReportBlock(string Type) : this(Type, default)
    {
    }
}
