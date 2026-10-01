using System.Buffers;

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
        ArgumentNullException.ThrowIfNull(options);
        Stream? staged = null;
        try
        {
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
            await CopyAsync(source, staged, options, cancellationToken).ConfigureAwait(false);
            staged.Position = 0;
            await source.DisposeAsync().ConfigureAwait(false);
            return new FileContextPdfSource(staged);
        }
        catch
        {
            try { if (staged is not null) { await staged.DisposeAsync().ConfigureAwait(false); } }
            finally { await source.DisposeAsync().ConfigureAwait(false); }
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

    private static async Task CopyAsync(Stream source, Stream staged, FileContextOptions options,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(options.PdfSourceBufferBytes);
        try
        {
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, options.PdfSourceBufferBytes), cancellationToken)
                .ConfigureAwait(false)) > 0)
            {
                if (staged.Length + read > options.MaximumPdfReadBytes)
                {
                    throw new IOException("The PDF exceeds the read limit.");
                }
                await staged.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
    }

    private static FileStream CreateTemporaryFile(FileContextOptions options)
    {
        var fileOptions = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            BufferSize = options.PdfSourceBufferBytes,
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
