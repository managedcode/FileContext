using System.Buffers;
using System.Text;

namespace ManagedCode.FileContext;

internal static class FileContextCsvWriter
{
    private const string RowEnding = "\r\n";
    private const string EscapedQuote = "\"\"";

    public static MemoryStream Create(FileContextCsvDocument document, long maximumBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Rows is null) { throw new ArgumentException("Document members cannot be null.", nameof(document)); }
        if (document.Delimiter is not ("," or ";" or "\t"))
        {
            throw new ArgumentException("CSV delimiter must be comma, semicolon or tab.", nameof(document));
        }
        var output = new MemoryStream();
        try
        {
            WriteRows(output, document, maximumBytes, cancellationToken);
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    private static void WriteRows(Stream output, FileContextCsvDocument document, long maximumBytes, CancellationToken token)
    {
        var bytes = (long)Encoding.UTF8.Preamble.Length;
        StorageFileCreator.EnsureWithinLimit(bytes, maximumBytes);
        using var writer = new StreamWriter(output, Encoding.UTF8, leaveOpen: true);
        var delimiter = document.Delimiter[0];
        var hasLimit = maximumBytes != long.MaxValue;
        var quoteTriggers = SearchValues.Create(stackalloc char[] { delimiter, '"', '\r', '\n' });
        foreach (var row in document.Rows)
        {
            token.ThrowIfCancellationRequested();
            if (row is null) { throw new ArgumentException("Document members cannot be null.", nameof(document)); }
            if (hasLimit)
            {
                bytes += RowEnding.Length;
                StorageFileCreator.EnsureWithinLimit(bytes, maximumBytes);
            }
            var first = true;
            foreach (var field in row)
            {
                token.ThrowIfCancellationRequested();
                var value = field.AsSpan();
                var quote = value.ContainsAny(quoteTriggers);
                if (hasLimit)
                {
                    bytes += (long)Encoding.UTF8.GetByteCount(value) + (quote ? EscapedQuote.Length + (long)value.Count('"') : 0) + (first ? 0 : 1);
                    StorageFileCreator.EnsureWithinLimit(bytes, maximumBytes);
                }
                if (!first) { writer.Write(delimiter); }
                WriteField(writer, value, quote);
                first = false;
            }
            writer.Write(RowEnding);
        }
    }

    private static void WriteField(StreamWriter writer, ReadOnlySpan<char> value, bool quote)
    {
        if (!quote)
        {
            writer.Write(value);
            return;
        }

        writer.Write('"');
        while (value.IndexOf('"') is var index && index >= 0)
        {
            writer.Write(value[..index]);
            writer.Write(EscapedQuote);
            value = value[(index + 1)..];
        }
        writer.Write(value);
        writer.Write('"');
    }
}
