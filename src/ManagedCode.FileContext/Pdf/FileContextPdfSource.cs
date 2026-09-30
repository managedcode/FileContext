namespace ManagedCode.FileContext.Pdf;

/// <summary>Owns a bounded seekable PDF source; non-seekable inputs are staged to disk.</summary>
public sealed class FileContextPdfSource : IAsyncDisposable
{
    private const int CopyBufferBytes = 81920;
    private const string TemporaryFilePrefix = "filecontext-pdf-";

    private FileContextPdfSource(Stream stream) => Stream = stream;

    public Stream Stream { get; }

    /// <summary>Takes ownership of the input, including on failure. Dispose the result after parsing.</summary>
    public static async Task<FileContextPdfSource> OpenAsync(Stream source, FileContextOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        Stream? staged = null;
        try
        {
            options.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            if (source.CanSeek)
            {
                Validate(source, options);
                source.Position = 0;
                return new FileContextPdfSource(source);
            }
            staged = CreateTemporaryFile();
            var buffer = new byte[CopyBufferBytes];
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (staged.Length + read > options.MaximumPdfReadBytes)
                {
                    throw new IOException("The PDF exceeds the read limit.");
                }
                await staged.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
            staged.Position = 0;
            await source.DisposeAsync().ConfigureAwait(false);
            return new FileContextPdfSource(staged);
        }
        catch
        {
            if (staged is not null) { await staged.DisposeAsync().ConfigureAwait(false); }
            await source.DisposeAsync().ConfigureAwait(false);
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
        if (source.Length > options.MaximumPdfReadBytes)
        {
            throw new IOException("The PDF exceeds the read limit.");
        }
        source.Position = 0;
    }

    private static FileStream CreateTemporaryFile() => new(
        Path.Combine(Path.GetTempPath(), TemporaryFilePrefix + Guid.NewGuid().ToString("N")),
        FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, CopyBufferBytes,
        FileOptions.Asynchronous | FileOptions.DeleteOnClose);

    public ValueTask DisposeAsync() => Stream.DisposeAsync();
}
