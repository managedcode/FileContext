using System.Text.Json;
namespace ManagedCode.FileContext;
/// <summary>A report block. Content follows the block JSON schema.</summary>
public sealed record FileContextReportBlock(string Type, JsonElement Content);
