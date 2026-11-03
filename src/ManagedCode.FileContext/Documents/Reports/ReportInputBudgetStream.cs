namespace ManagedCode.FileContext;

/// <summary>Counts serialized report bytes without retaining them.</summary>
internal sealed class ReportInputBudgetStream(int maximumBytes, CancellationToken token) : Stream
{
    private long _written;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        token.ThrowIfCancellationRequested();
        if (buffer.Length > maximumBytes - _written)
        {
            throw new InvalidDataException($"Report input exceeds {maximumBytes} bytes.");
        }
        _written += buffer.Length;
    }

    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
