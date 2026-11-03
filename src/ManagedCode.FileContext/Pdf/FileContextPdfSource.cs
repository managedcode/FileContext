namespace ManagedCode.FileContext.Pdf;

/// <summary>Owns a bounded local PDF source; cloud inputs are staged before synchronous random reads.</summary>
public sealed class FileContextPdfSource : IAsyncDisposable
{
    private const string TemporaryFilePrefix = "filecontext-pdf-";

    private FileContextPdfSource(Stream stream) => Stream = stream;

    public Stream Stream { get; }

    /// <summary>Takes ownership of the input, including on failure. Dispose the result after parsing.</summary>
    public static async Task<FileContextPdfSource> OpenAsync(Stream source, FileContextOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        Stream? staged = null;
        var disposeSourceOnFailure = true;
        try
        {
            ArgumentNullException.ThrowIfNull(options);
            options.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            if (source.CanSeek)
            {
                Validate(source, options);
            }
            else if (!source.CanRead)
            {
                throw new ArgumentException("The PDF source must be readable.", nameof(source));
            }
            if (options.PdfSourceStagingMode == FileContextPdfSourceStagingMode.Automatic
                && source.CanSeek && source is FileStream or MemoryStream)
            {
                return new FileContextPdfSource(source);
            }
            staged = CreateTemporaryFile(options);
            using var bounded = new StorageReadBudgetStream(source, options.PdfSourceReadBudget);
            await bounded.CopyToAsync(staged, options.PdfSourceBufferBytes, cancellationToken).ConfigureAwait(false);
            staged.Position = 0;
            disposeSourceOnFailure = false;
            await source.DisposeAsync().ConfigureAwait(false);
            return new FileContextPdfSource(staged);
        }
        catch
        {
            try { if (staged is not null) { await staged.DisposeAsync().ConfigureAwait(false); } }
            finally
            {
                if (disposeSourceOnFailure) { await source.DisposeAsync().ConfigureAwait(false); }
            }
            throw;
        }
    }

    internal static void Validate(Stream source, FileContextOptions options)
    {
        if (!source.CanRead || !source.CanSeek)
        {
            throw new ArgumentException("PDF parsing requires a readable seekable stream. Stage it with FileContextPdfSource.OpenAsync.", nameof(source));
        }
        options.Validate();
        if (source.Length > options.PdfSourceReadBudget)
        {
            throw new IOException("The PDF exceeds the read limit.");
        }
        source.Position = 0;
    }

    private static FileStream CreateTemporaryFile(FileContextOptions options)
    {
        var fileOptions = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            BufferSize = Math.Min(options.PdfSourceBufferBytes, options.MaximumPdfReadBytes),
            Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose
        };
        if (!OperatingSystem.IsWindows())
        {
            fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }
        return new FileStream(Path.Combine(options.PdfTemporaryDirectory ?? Path.GetTempPath(),
            TemporaryFilePrefix + Guid.NewGuid().ToString("N")), fileOptions);
    }

    public ValueTask DisposeAsync() => Stream.DisposeAsync();
}
