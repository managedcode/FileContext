using System.Text;

namespace ManagedCode.FileContext;

internal static class FileContextCsvWriter
{
    public static byte[] Create(FileContextCsvDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Rows is null) { throw new ArgumentException("Document members cannot be null.", nameof(document)); }
        if (document.Delimiter is not ("," or ";" or "\t"))
        {
            throw new ArgumentException("CSV delimiter must be comma, semicolon or tab.", nameof(document));
        }
        var text = new StringBuilder();
        foreach (var row in document.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row is null) { throw new ArgumentException("Document members cannot be null.", nameof(document)); }
            text.AppendJoin(document.Delimiter, row.Select(value => Escape(value ?? string.Empty, document.Delimiter)));
            text.Append("\r\n");
        }
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text.ToString())).ToArray();
    }

    private static string Escape(string value, string delimiter) =>
        value.Contains(delimiter, StringComparison.Ordinal) || value.IndexOfAny(['"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
}
