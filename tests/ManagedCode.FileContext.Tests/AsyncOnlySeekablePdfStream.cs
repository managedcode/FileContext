namespace ManagedCode.FileContext.Tests;

// Real file contents, with the async-read/synchronous-seek separation of a cloud source.
internal sealed class AsyncOnlySeekablePdfStream(FileStream source) : Stream
{
    public bool WasDisposed { get; private set; }
    public int MaximumReadRequestBytes { get; private set; }
    public CancellationTokenSource? CancelAfterRead { get; set; }
    public bool FailRead { get; set; }
    public bool HideSeekability { get; set; }
    public override bool CanRead => source.CanRead;
    public override bool CanSeek => !HideSeekability;
    public override bool CanWrite => false;
    public override long Length => source.Length;
    public override long Position { get => source.Position; set => source.Position = value; }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        MaximumReadRequestBytes = Math.Max(MaximumReadRequestBytes, buffer.Length);
        if (FailRead) { throw new IOException("Source read failed."); }
        var count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (CancelAfterRead is { } cancellation) { await cancellation.CancelAsync().ConfigureAwait(false); }
        return count;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new InvalidOperationException("The parser must never read this remote source synchronously.");
    public override long Seek(long offset, SeekOrigin origin) => source.Seek(offset, origin);
    public override void Flush() => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        WasDisposed = true;
        if (disposing) { source.Dispose(); }
        base.Dispose(disposing);
    }
}
