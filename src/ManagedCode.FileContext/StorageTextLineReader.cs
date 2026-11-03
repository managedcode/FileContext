using System.Text;

namespace ManagedCode.FileContext;

internal sealed class StorageTextLineReader(StreamReader reader, long maximumBytes)
{
    private readonly char[] _buffer = new char[4_096];
    private int _position;
    private int _length;
    private bool _skipLineFeed;

    private ReadOnlySpan<char> BufferedCharacters => _buffer.AsSpan(_position, _length - _position);

    public async ValueTask<bool> HasNextLineAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_skipLineFeed)
        {
            _skipLineFeed = false;
            if (await EnsureBufferedAsync(cancellationToken).ConfigureAwait(false) && _buffer[_position] == '\n')
            {
                _position++;
            }
        }

        return await EnsureBufferedAsync(cancellationToken).ConfigureAwait(false);
    }

    // The returned memory is borrowed until the next read.
    public bool TryReadBufferedLine(out ReadOnlyMemory<char> line)
    {
        var lineBreak = BufferedCharacters.IndexOfAny('\r', '\n');
        if (lineBreak < 0)
        {
            line = default;
            return false;
        }

        line = _buffer.AsMemory(_position, lineBreak);
        _position += lineBreak;
        ConsumeLineBreak();
        return true;
    }

    // The caller has already established that a line is available with HasNextLineAsync.
    public async ValueTask<long> ReadLineAsync(StringBuilder? destination, long bytesAlreadyCaptured, CancellationToken cancellationToken)
    {
        var counter = new Utf8ByteCounter();
        do
        {
            var available = BufferedCharacters;
            var lineBreak = available.IndexOfAny('\r', '\n');
            var chunk = lineBreak < 0 ? available : available[..lineBreak];
            if (destination is not null)
            {
                counter.Append(chunk);
                EnsureWithinLimit(bytesAlreadyCaptured, counter.TotalBytes);
                destination.Append(chunk);
            }

            _position += chunk.Length;
            if (lineBreak >= 0)
            {
                ConsumeLineBreak();
                break;
            }
        }
        while (await EnsureBufferedAsync(cancellationToken).ConfigureAwait(false));

        return counter.TotalBytes;
    }

    private void ConsumeLineBreak() => _skipLineFeed = _buffer[_position++] == '\r';

    private void EnsureWithinLimit(long capturedBytes, long lineBytes)
    {
        if (lineBytes > maximumBytes - capturedBytes)
        {
            throw new IOException($"Requested range exceeds the configured {maximumBytes}-byte limit.");
        }
    }

    private async ValueTask<bool> EnsureBufferedAsync(CancellationToken cancellationToken)
    {
        if (_position == _length)
        {
            _length = await reader.ReadAsync(_buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            _position = 0;
        }

        return _length > 0;
    }
}
