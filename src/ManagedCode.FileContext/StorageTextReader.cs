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
        var encoder = Encoding.UTF8.GetEncoder();
        var encodedBuffer = new byte[Encoding.UTF8.GetMaxByteCount(CharacterBufferLength)];
        var builder = new StringBuilder();
        var bytesRead = 0L;

        while (await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false) is var read && read > 0)
        {
            bytesRead += encoder.GetBytes(buffer.AsSpan(0, read), encodedBuffer, flush: false);
            EnsureWithinLimit(bytesRead, limit);

            builder.Append(buffer, 0, read);
        }

        bytesRead += encoder.GetBytes(ReadOnlySpan<char>.Empty, encodedBuffer, flush: true);
        EnsureWithinLimit(bytesRead, limit);
        return builder.ToString();
    }

    private static void EnsureWithinLimit(long bytesRead, long limit)
    {
        if (bytesRead > limit)
        {
            throw new IOException($"Decoded file content exceeds the configured {limit}-byte limit.");
        }
    }
}
