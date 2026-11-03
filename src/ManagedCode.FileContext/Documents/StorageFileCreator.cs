using ManagedCode.Storage.Core;
using ManagedCode.Storage.Core.Models;

namespace ManagedCode.FileContext;

internal sealed class StorageFileCreator(IStorage storage, StoragePathScope paths)
{
    public async Task<FileContextCreatedFile> CreateAsync(string fileName, Stream source, string mediaType,
        long maximumBytes, CancellationToken cancellationToken)
    {
        var path = $"outputs/{Guid.NewGuid():N}/{StoragePathScope.Normalize(fileName)}";
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        using var buffer = new MemoryStream();
        using var bounded = new StorageReadBudgetStream(source, maximumBytes);
        await bounded.CopyToAsync(buffer, StorageReadBudgetStream.DefaultCopyBufferSize, cancellationToken).ConfigureAwait(false);
        return await UploadAsync(path, buffer, mediaType, cancellationToken).ConfigureAwait(false);
    }

    public Task<FileContextCreatedFile> CreatePreparedAsync(string fileName, MemoryStream buffer, string mediaType,
        long maximumBytes, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        var path = $"outputs/{Guid.NewGuid():N}/{StoragePathScope.Normalize(fileName)}";
        EnsureWithinLimit(buffer.Length, maximumBytes);
        return UploadAsync(path, buffer, mediaType, cancellationToken);
    }

    public static void EnsureWithinLimit(long bytes, long maximumBytes)
    {
        if (bytes > maximumBytes) { throw new IOException($"Generated file exceeds the {maximumBytes}-byte output budget."); }
    }

    private async Task<FileContextCreatedFile> UploadAsync(string path, MemoryStream buffer, string mediaType, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var storagePath = paths.ToStoragePath(path);
        var length = buffer.Length;
        buffer.Position = 0;
        StorageResult.EnsureSuccess(await storage.UploadAsync(buffer,
            new UploadOptions(storagePath, mimeType: mediaType), cancellationToken).ConfigureAwait(false), "create generated file");
        return new FileContextCreatedFile(path, mediaType, length);
    }
}
