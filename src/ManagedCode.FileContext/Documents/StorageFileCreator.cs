using ManagedCode.Storage.Core;
using ManagedCode.Storage.Core.Models;

namespace ManagedCode.FileContext;

internal sealed class StorageFileCreator(IStorage storage, StoragePathScope paths)
{
    private const int BufferSize = 81_920;

    public async Task<FileContextCreatedFile> CreateAsync(string fileName, Stream source, string mediaType,
        long maximumBytes, CancellationToken cancellationToken)
    {
        var path = $"outputs/{Guid.NewGuid():N}/{StoragePathScope.Normalize(fileName)}";
        var storagePath = paths.ToStoragePath(path);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        using var buffer = new MemoryStream();
        var chunk = new byte[BufferSize];
        int count;
        while ((count = await source.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length > maximumBytes - count) { throw new IOException($"Generated file exceeds the {maximumBytes}-byte output budget."); }
            await buffer.WriteAsync(chunk.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
        }
        buffer.Position = 0;
        StorageResult.EnsureSuccess(await storage.UploadAsync(buffer,
            new UploadOptions(storagePath, mimeType: mediaType), cancellationToken).ConfigureAwait(false), "create generated file");
        return new FileContextCreatedFile(path, mediaType, buffer.Length);
    }
}
