using System.Text.Json;
namespace ManagedCode.FileContext;
#pragma warning disable MA0015
internal static class ReportJson
{
    public static string Text(JsonElement value, string name, string fallback = "") =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String
            ? item.GetString() ?? fallback : fallback;

    public static IReadOnlyList<string> Strings(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.Array
            ? item.EnumerateArray().Select(entry => entry.ToString()).ToArray() : [];

    public static IReadOnlyList<double> Numbers(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.Array
            ? item.EnumerateArray().Select(entry => entry.ValueKind == JsonValueKind.Number && entry.TryGetDouble(out var number)
                ? number : throw new ArgumentException($"{name} must contain only numbers.")).ToArray() : [];

    public static IReadOnlyList<JsonElement> Array(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.Array
            ? item.EnumerateArray().ToArray() : [];

    public static JsonElement Property(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item) ? item : default;
    public static string FormatCell(JsonElement row, JsonElement column)
    {
        var key = Text(column, "key");
        var value = Property(row, key);
        if (value.ValueKind == JsonValueKind.Undefined) { return string.Empty; }
        var format = Text(column, "format");
        if (format.Length > 0 && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            try { return number.ToString(format, System.Globalization.CultureInfo.InvariantCulture); }
            catch (FormatException error) { throw new ArgumentException($"table column '{key}' has invalid number format.", nameof(column), error); }
        }
        return value.ToString();
    }

    public static bool IsHighlighted(JsonElement table, int rowIndex, string key) =>
        Array(table, "highlightCells").Any(item =>
            Property(item, "row").ValueKind == JsonValueKind.Number &&
            Property(item, "row").TryGetInt32(out var row) && row == rowIndex &&
            string.Equals(Text(item, "column"), key, StringComparison.Ordinal));
}
