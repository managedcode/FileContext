using System.Text;

namespace ManagedCode.FileContext;

internal static class StorageTextReader
{
    private const int ReaderBufferSize = 1_024;
    private const int CharacterBufferLength = 4_096;

    public static async Task<string> ReadAsync(
        Stream stream,
        long limit,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true, ReaderBufferSize, leaveOpen: true);
        var buffer = new char[CharacterBufferLength];
        var builder = new StringBuilder();
        var bytesRead = 0L;

        while (await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false) is var read && read > 0)
        {
            bytesRead += Encoding.UTF8.GetByteCount(buffer.AsSpan(0, read));
            if (bytesRead > limit)
            {
                throw new IOException($"Decoded file content exceeds the configured {limit}-byte limit.");
            }

            builder.Append(buffer, 0, read);
        }

        return builder.ToString();
    }
}
