using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace ManagedCode.FileContext;

internal static class FileContextCsvTables
{
    public static FileContextTablesInfo Read(Stream source, string path, int? headerRow,
        string? delimiter, CancellationToken token)
    {
        delimiter ??= DetectDelimiter(source, headerRow, token);
        if (delimiter.Length != 1 || delimiter[0] is '"' or '\r' or '\n' or '\0')
        { throw new ArgumentException("CSV delimiter must be one character other than quote, newline or NUL.", nameof(delimiter)); }
        source.Position = 0;
        using var reader = CreateReader(source);
        var parser = new CsvRecordReader(reader, delimiter[0], token);
        IReadOnlyList<string>? headers = null;
        var record = 0;
        int? foundHeader = null;
        long rows = 0;
        var width = 0;
        while (parser.TryRead(headers is null && record >= (headerRow ?? 1) - 1, out var fields))
        {
            record++;
            if (headerRow.HasValue && record < headerRow.Value) { continue; }
            if (headers is null)
            {
                if (!headerRow.HasValue && !fields.HasValue) { continue; }
                headers = fields.Fields!;
                foundHeader = record;
                width = fields.ColumnCount;
                continue;
            }
            width = Math.Max(width, fields.ColumnCount);
            if (fields.HasValue) { rows++; }
        }
        if (headerRow.HasValue && headers is null)
        { throw new ArgumentException("The requested CSV header record does not exist.", nameof(headerRow)); }
        var names = Enumerable.Range(0, width).Select(index => index < (headers?.Count ?? 0) ? headers![index] : string.Empty).ToArray();
        return new(path, "csv", delimiter, [new(Path.GetFileName(path), null, foundHeader, 1, names, rows)]);
    }

    private static StreamReader CreateReader(Stream source) =>
        new(source, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, leaveOpen: true);

    private static string DetectDelimiter(Stream source, int? headerRow, CancellationToken token)
    {
        var best = ",";
        var columns = 0;
        foreach (var candidate in new[] { ",", ";", "\t", "|" })
        {
            token.ThrowIfCancellationRequested();
            source.Position = 0;
            using var reader = CreateReader(source);
            var parser = new CsvRecordReader(reader, candidate[0], token);
            try
            {
                var record = 0;
                while (parser.TryRead(captureFields: false, out var fields))
                {
                    record++;
                    if (record < (headerRow ?? 1)) { continue; }
                    if (fields.ColumnCount > columns) { best = candidate; columns = fields.ColumnCount; }
                    break;
                }
            }
            catch (MalformedLineException) { /* A different delimiter can make a quoted field invalid. */ }
        }
        return best;
    }
}
