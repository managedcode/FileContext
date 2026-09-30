namespace ManagedCode.FileContext.Pdf;

/// <summary>Bounds concurrent PDF source reads and processing across scoped file contexts.</summary>
public sealed class FileContextPdfProcessor : IDisposable
{
    /// <summary>Shared default processor for manually composed file contexts.</summary>
    public static FileContextPdfProcessor Shared { get; } = new(new FileContextOptions());

    private readonly SemaphoreSlim _permits;

    public FileContextPdfProcessor(FileContextOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _permits = new SemaphoreSlim(options.MaximumConcurrentPdfOperations, options.MaximumConcurrentPdfOperations);
    }

    /// <summary>Waits before downloading or buffering a PDF. Dispose the permit after processing completes.</summary>
    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await _permits.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Permit(_permits);
    }

    public void Dispose() => _permits.Dispose();

    private sealed class Permit(SemaphoreSlim permits) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            permits.Release();
        }
    }
}
