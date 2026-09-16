using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;

namespace ManagedCode.FileContext;

/// <summary>Inspects caller-owned, seekable source streams. The caller retains ownership and authorizes access.</summary>
public static class FileContextTableReader
{
    public static FileContextTablesInfo Read(Stream source, string fileName, int? headerRow = null,
        string? delimiter = null, FileContextOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        Validate(fileName, headerRow);
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new();
        options.Validate();
        if (!source.CanSeek) { throw new ArgumentException("Table inspection requires a seekable source stream.", nameof(source)); }
        if (source.Length > options.MaximumFullReadBytes) { throw new IOException("Table source exceeds the configured read budget."); }
        source.Position = 0;
        FileContextTablesInfo result;
        if (string.Equals(Path.GetExtension(fileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            using var workbook = SpreadsheetDocument.Open(source, false);
            result = new(fileName, "xlsx", null, FileContextWorkbookTables.Read(workbook, headerRow, cancellationToken));
        }
        else { result = FileContextCsvTables.Read(source, fileName, headerRow, delimiter, cancellationToken); }
        if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(result)) > options.MaximumRangeReadBytes)
        { throw new IOException("Table metadata exceeds the configured output read budget."); }
        return result;
    }

    internal static void Validate(string fileName, int? headerRow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (headerRow is < 1) { throw new ArgumentOutOfRangeException(nameof(headerRow)); }
        if (Path.GetExtension(fileName).ToLowerInvariant() is not (".xlsx" or ".csv"))
        { throw new ArgumentException("Table inspection supports .xlsx and .csv files.", nameof(fileName)); }
    }
}
