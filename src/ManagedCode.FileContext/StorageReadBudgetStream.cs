using System.Buffers;

namespace ManagedCode.FileContext;

/// <summary>Limits source bytes read without owning the underlying storage stream.</summary>
internal sealed class StorageReadBudgetStream(Stream source, long maximumBytes) : Stream
{
    public const int DefaultCopyBufferSize = 81_920;

    private readonly long _maximumBytes = maximumBytes;
    private long _remaining = maximumBytes;

    public override bool CanRead => source.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer) => Account(source.Read(buffer[..GetReadSize(buffer.Length)]));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var count = await source.ReadAsync(buffer[..GetReadSize(buffer.Length)], cancellationToken).ConfigureAwait(false);
        return Account(count);
    }

    public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize);
        var chunkSize = (int)Math.Min(bufferSize, _maximumBytes);
        var chunk = ArrayPool<byte>.Shared.Rent(chunkSize);
        try
        {
            int count;
            while ((count = await ReadAsync(chunk.AsMemory(0, chunkSize), cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(chunk.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk, clearArray: true);
        }
    }

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    // Allow one extra byte to distinguish an exact-fit source from a source that exceeds its budget.
    private int GetReadSize(int requested) => _remaining >= requested ? requested : (int)_remaining + 1;

    private int Account(int bytesRead)
    {
        if (bytesRead > _remaining)
        {
            throw new IOException($"The source exceeds the configured {_maximumBytes}-byte read limit.");
        }

        _remaining -= bytesRead;
        return bytesRead;
    }
}
