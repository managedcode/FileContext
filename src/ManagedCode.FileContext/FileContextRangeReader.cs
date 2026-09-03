using System.Text;

namespace ManagedCode.FileContext;

internal sealed class FileContextRangeReader(long maximumBytes)
{
    public async Task<FileContextRange> ReadAsync(
        StreamReader reader,
        string path,
        int startLine,
        int lineCount,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var lineReader = new StorageTextLineReader(reader);
        var currentLine = 0;
        var linesAdded = 0;
        var bytesAdded = 0L;
        var separatorBytes = Encoding.UTF8.GetByteCount(Environment.NewLine);

        while (true)
        {
            var captureContent = currentLine + FileContextDefaults.FirstLineNumber >= startLine && linesAdded < lineCount;
            var bytesBeforeLine = bytesAdded + (captureContent && linesAdded > 0 ? separatorBytes : 0);
            var line = await lineReader
                .ReadAsync(captureContent, maximumBytes, bytesBeforeLine, cancellationToken)
                .ConfigureAwait(false);
            if (!line.HasLine)
            {
                return CreateCompleted(path, startLine, currentLine, linesAdded, builder);
            }

            currentLine++;
            if (currentLine < startLine)
            {
                continue;
            }

            if (linesAdded == lineCount)
            {
                return CreateContinuation(path, startLine, currentLine, builder);
            }

            if (linesAdded > 0)
            {
                builder.AppendLine();
                bytesAdded += separatorBytes;
            }

            builder.Append(line.Content);
            bytesAdded += line.Utf8Bytes;
            linesAdded++;
        }
    }

    private static FileContextRange CreateContinuation(
        string path,
        int startLine,
        int currentLine,
        StringBuilder content)
    {
        return new FileContextRange(
            path,
            startLine,
            currentLine - FileContextDefaults.FirstLineNumber,
            null,
            true,
            content.ToString());
    }

    private static FileContextRange CreateCompleted(
        string path,
        int startLine,
        int totalLines,
        int linesAdded,
        StringBuilder content)
    {
        var endLine = linesAdded == 0
            ? startLine - FileContextDefaults.FirstLineNumber
            : startLine + linesAdded - FileContextDefaults.FirstLineNumber;
        return new FileContextRange(path, startLine, endLine, totalLines, false, content.ToString());
    }
}
