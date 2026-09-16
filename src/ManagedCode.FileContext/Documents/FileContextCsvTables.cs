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
        using var parser = Create(source, delimiter);
        string[]? headers = null;
        var record = 0;
        int? foundHeader = null;
        long rows = 0;
        var width = 0;
        while (!parser.EndOfData)
        {
            token.ThrowIfCancellationRequested();
            var fields = parser.ReadFields()!;
            record++;
            if (headerRow.HasValue && record < headerRow.Value) { continue; }
            if (headers is null)
            {
                if (!headerRow.HasValue && fields.All(string.IsNullOrEmpty)) { continue; }
                headers = fields;
                foundHeader = record;
                width = fields.Length;
                continue;
            }
            width = Math.Max(width, fields.Length);
            if (fields.Any(static field => field.Length > 0)) { rows++; }
        }
        if (headerRow.HasValue && headers is null)
        { throw new ArgumentException("The requested CSV header record does not exist.", nameof(headerRow)); }
        var names = Enumerable.Range(0, width).Select(index => index < (headers?.Length ?? 0) ? headers![index] : string.Empty).ToArray();
        return new(path, "csv", delimiter, [new(Path.GetFileName(path), null, foundHeader, 1, names, rows)]);
    }

    private static TextFieldParser Create(Stream source, string delimiter)
    {
        var parser = new TextFieldParser(source, new UTF8Encoding(false, true), true, true)
        { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(delimiter);
        return parser;
    }

    private static string DetectDelimiter(Stream source, int? headerRow, CancellationToken token)
    {
        var best = ",";
        var columns = 0;
        foreach (var candidate in new[] { ",", ";", "\t", "|" })
        {
            token.ThrowIfCancellationRequested();
            source.Position = 0;
            using var parser = Create(source, candidate);
            try
            {
                var record = 0;
                while (!parser.EndOfData)
                {
                    token.ThrowIfCancellationRequested();
                    var fields = parser.ReadFields()!;
                    record++;
                    if (record < (headerRow ?? 1)) { continue; }
                    if (fields.Length > columns) { best = candidate; columns = fields.Length; }
                    break;
                }
            }
            catch (MalformedLineException) { /* A different delimiter can make a quoted field invalid. */ }
        }
        return best;
    }
}
