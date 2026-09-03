using System.Text;

namespace ManagedCode.FileContext;

internal sealed class StorageTextLineReader(StreamReader reader)
{
    private const int EndOfStream = -1;
    private const int Utf8AsciiBytes = 1;
    private const int Utf8TwoByteCharacterBytes = 2;
    private const int Utf8ThreeByteCharacterBytes = 3;
    private const int Utf8SurrogatePairBytes = 4;
    private const char Utf8AsciiMaximum = '\u007f';
    private const char Utf8TwoByteCharacterMaximum = '\u07ff';
    private readonly char[] _characterBuffer = new char[1];
    private char? _pendingCharacter;

    public async ValueTask<(bool HasLine, string Content, long Utf8Bytes)> ReadAsync(
        bool captureContent,
        long maximumBytes,
        long bytesAlreadyCaptured,
        CancellationToken cancellationToken)
    {
        var builder = captureContent ? new StringBuilder() : null;
        var lineBytes = 0L;
        var hasPendingHighSurrogate = false;
        var readAny = false;

        while (await ReadCharacterAsync(cancellationToken).ConfigureAwait(false) is var value && value != EndOfStream)
        {
            readAny = true;
            var character = (char)value;
            if (character is '\r' or '\n')
            {
                await ConsumeLineFeedAfterCarriageReturnAsync(character, cancellationToken).ConfigureAwait(false);
                lineBytes += hasPendingHighSurrogate ? Utf8ThreeByteCharacterBytes : 0;
                EnsureWithinLimit(captureContent, maximumBytes, bytesAlreadyCaptured + lineBytes);
                return (true, builder?.ToString() ?? string.Empty, lineBytes);
            }

            if (captureContent)
            {
                builder!.Append(character);
                lineBytes += CountUtf8Bytes(character, ref hasPendingHighSurrogate);
                EnsureWithinLimit(true, maximumBytes, bytesAlreadyCaptured + lineBytes);
            }
        }

        lineBytes += hasPendingHighSurrogate ? Utf8ThreeByteCharacterBytes : 0;
        EnsureWithinLimit(captureContent, maximumBytes, bytesAlreadyCaptured + lineBytes);
        return readAny
            ? (true, builder?.ToString() ?? string.Empty, lineBytes)
            : (false, string.Empty, 0);
    }

    private async ValueTask<int> ReadCharacterAsync(CancellationToken cancellationToken)
    {
        if (_pendingCharacter is { } pending)
        {
            _pendingCharacter = null;
            return pending;
        }

        var count = await reader.ReadAsync(_characterBuffer.AsMemory(), cancellationToken).ConfigureAwait(false);
        return count == 0 ? EndOfStream : _characterBuffer[0];
    }

    private async ValueTask ConsumeLineFeedAfterCarriageReturnAsync(
        char lineBreak,
        CancellationToken cancellationToken)
    {
        if (lineBreak != '\r')
        {
            return;
        }

        var next = await ReadCharacterAsync(cancellationToken).ConfigureAwait(false);
        if (next != EndOfStream && next != '\n')
        {
            _pendingCharacter = (char)next;
        }
    }

    private static int CountUtf8Bytes(char character, ref bool hasPendingHighSurrogate)
    {
        var byteCount = 0;
        if (hasPendingHighSurrogate)
        {
            hasPendingHighSurrogate = false;
            if (char.IsLowSurrogate(character))
            {
                return Utf8SurrogatePairBytes;
            }

            byteCount = Utf8ThreeByteCharacterBytes;
        }

        if (char.IsHighSurrogate(character))
        {
            hasPendingHighSurrogate = true;
            return byteCount;
        }

        if (character <= Utf8AsciiMaximum)
        {
            return byteCount + Utf8AsciiBytes;
        }

        return byteCount + (character <= Utf8TwoByteCharacterMaximum
            ? Utf8TwoByteCharacterBytes
            : Utf8ThreeByteCharacterBytes);
    }

    private static void EnsureWithinLimit(bool captureContent, long maximumBytes, long capturedBytes)
    {
        if (captureContent && capturedBytes > maximumBytes)
        {
            throw new IOException($"Requested range exceeds the configured {maximumBytes}-byte limit.");
        }
    }
}
