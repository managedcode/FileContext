using ManagedCode.Storage.Core;
using ManagedCode.Storage.Core.Models;
using Microsoft.Agents.AI;

namespace ManagedCode.FileContext;

/// <summary>Adapts a ManagedCode.Storage <see cref="IStorage" /> to Agent Framework's file-store contract.</summary>
public sealed class ManagedCodeStorageFileStore : AgentFileStore
{
    private readonly IStorage _storage;
    private readonly FileContextOptions _options;
    private readonly StoragePathScope _paths;

    /// <summary>Creates a storage-backed agent file store.</summary>
    public ManagedCodeStorageFileStore(IStorage storage, FileContextOptions? options = null)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _options = options ?? new FileContextOptions();
        _options.Validate();
        _paths = new StoragePathScope(_options.RootPrefix);
    }

    /// <summary>Creates a uniquely named binary file. The stream remains owned by the caller.</summary>
    public Task<FileContextCreatedFile> CreateFileAsync(string fileName, Stream content, string mediaType,
        long? maximumBytes = null, CancellationToken cancellationToken = default)
    {
        if (!_options.EnableWriteTools) { throw new InvalidOperationException("File creation requires EnableWriteTools."); }
        return FileContextOperation.RunAsync(_options.OperationTimeout,
            token => new StorageFileCreator(_storage, _paths).CreateAsync(fileName, content, mediaType,
                Math.Min(maximumBytes ?? _options.MaximumGeneratedFileBytes, _options.MaximumGeneratedFileBytes), token), cancellationToken);
    }

    internal Task<FileContextCreatedFile> CreatePreparedFileAsync(string fileName, MemoryStream content, string mediaType,
        long maximumBytes, CancellationToken cancellationToken)
    {
        var limit = GetGeneratedFileLimit(maximumBytes);
        return FileContextOperation.RunAsync(_options.OperationTimeout,
            token => new StorageFileCreator(_storage, _paths).CreatePreparedAsync(fileName, content, mediaType,
                limit, token), cancellationToken);
    }

    internal long GetGeneratedFileLimit(long maximumBytes)
    {
        if (!_options.EnableWriteTools) { throw new InvalidOperationException("File creation requires EnableWriteTools."); }
        return Math.Min(maximumBytes, _options.MaximumGeneratedFileBytes);
    }

    public override Task WriteAsync(string path, string content, CancellationToken cancellationToken = default)
        => FileContextOperation.RunAsync(_options.OperationTimeout, token => WriteOperationAsync(path, content, token), cancellationToken);

    public override Task<string?> ReadAsync(string path, CancellationToken cancellationToken = default)
        => FileContextOperation.RunAsync(_options.OperationTimeout, token => ReadOperationAsync(path, token), cancellationToken);

    public override Task<bool> DeleteAsync(string path, CancellationToken cancellationToken = default)
        => FileContextOperation.RunAsync(_options.OperationTimeout, token => DeleteOperationAsync(path, token), cancellationToken);

    public override Task<IReadOnlyList<FileStoreEntry>> ListChildrenAsync(string directory, CancellationToken cancellationToken = default)
        => FileContextOperation.RunAsync(_options.OperationTimeout, token => ListChildrenOperationAsync(directory, token), cancellationToken);

    public override Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default)
        => FileContextOperation.RunAsync(_options.OperationTimeout, token => FileExistsInOperationAsync(path, token), cancellationToken);

    public override Task<IReadOnlyList<FileSearchResult>> SearchAsync(
        string directory, string regexPattern, string? globPattern = null, bool recursive = false, CancellationToken cancellationToken = default)
        => FileContextOperation.RunAsync(_options.OperationTimeout, token => SearchOperationAsync(directory, regexPattern, globPattern, recursive, token), cancellationToken);

    public override Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
        => FileContextOperation.RunAsync(_options.OperationTimeout, token => CreateDirectoryOperationAsync(path, token), cancellationToken);

    private async Task WriteOperationAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var storagePath = _paths.ToStoragePath(path);
        var result = await _storage.UploadAsync(
            content,
            new UploadOptions { FileName = storagePath },
            cancellationToken).ConfigureAwait(false);
        StorageResult.EnsureSuccess(result, $"write {path}");
    }

    private async Task<string?> ReadOperationAsync(string path, CancellationToken cancellationToken = default)
    {
        var storagePath = _paths.ToStoragePath(path);
        StorageTextPolicy.RequireText(storagePath);
        if (!await ExistsCoreAsync(storagePath, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var limit = _options.MaximumFullReadBytes;
        if (limit != long.MaxValue)
        {
            var metadata = await GetMetadataCoreAsync(storagePath, cancellationToken).ConfigureAwait(false);
            if (metadata.Length > (ulong)limit)
            {
                throw new IOException($"File '{path}' is {metadata.Length} bytes; the full-read limit is {limit} bytes. Use file_context_read_range.");
            }
        }

        var stream = await OpenReadCoreAsync(storagePath, cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var bounded = limit == long.MaxValue ? null : new StorageReadBudgetStream(stream, limit);
            return await StorageTextReader.ReadAsync(bounded ?? stream, limit, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> DeleteOperationAsync(string path, CancellationToken cancellationToken = default)
    {
        var storagePath = _paths.ToStoragePath(path);
        var result = await _storage.DeleteAsync(storagePath, cancellationToken).ConfigureAwait(false);
        StorageResult.EnsureSuccess(result, $"delete {path}");
        return result.Value;
    }

    private Task<IReadOnlyList<FileStoreEntry>> ListChildrenOperationAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        var normalizedDirectory = StoragePathScope.Normalize(directory, allowEmpty: true);
        return StorageDirectoryListing.ReadAsync(EnumerateScopedFilesAsync(normalizedDirectory, cancellationToken),
            normalizedDirectory, _options.MaximumDirectoryEntries, cancellationToken);
    }

    internal Task<bool> FileExistsInOperationAsync(string path, CancellationToken cancellationToken) =>
        ExistsCoreAsync(_paths.ToStoragePath(path), cancellationToken);

    private Task<IReadOnlyList<FileSearchResult>> SearchOperationAsync(
        string directory,
        string regexPattern,
        string? globPattern = null,
        bool recursive = false,
        CancellationToken cancellationToken = default)
        => new StorageFileSearcher(this, _options)
            .SearchAsync(directory, regexPattern, globPattern, recursive, cancellationToken);

    private Task CreateDirectoryOperationAsync(string path, CancellationToken cancellationToken = default)
    {
        _ = _paths.ToStoragePath(path, allowEmpty: true);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <summary>Opens a scoped stream owned by the caller. The deadline covers opening the stream.</summary>
    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default) =>
        FileContextOperation.RunAsync(_options.OperationTimeout, token => OpenReadInOperationAsync(path, token),
            cancellationToken, static stream => stream.DisposeAsync());

    internal Task<Stream> OpenReadInOperationAsync(string path, CancellationToken cancellationToken) =>
        OpenReadCoreAsync(_paths.ToStoragePath(path), cancellationToken);

    internal async Task<BlobMetadata?> GetMetadataAsync(string path, CancellationToken cancellationToken)
    {
        var storagePath = _paths.ToStoragePath(path);
        return await ExistsCoreAsync(storagePath, cancellationToken).ConfigureAwait(false)
            ? await GetMetadataCoreAsync(storagePath, cancellationToken).ConfigureAwait(false)
            : null;
    }

    internal async IAsyncEnumerable<(string Path, BlobMetadata Metadata)> EnumerateScopedFilesAsync(
        string directory,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var storageDirectory = _paths.ToStoragePath(directory, allowEmpty: true);
        await foreach (var metadata in _storage.GetBlobMetadataListAsync(storageDirectory, cancellationToken).ConfigureAwait(false))
        {
            var path = _paths.FromStoragePath(metadata.FullName);
            if (path is not null && StoragePathScope.IsDescendant(path, directory))
            {
                yield return (path, metadata);
            }
        }
    }

    private async Task<bool> ExistsCoreAsync(string storagePath, CancellationToken cancellationToken)
    {
        var result = await _storage.ExistsAsync(storagePath, cancellationToken).ConfigureAwait(false);
        return StorageResult.GetValue(result, $"check {storagePath}");
    }

    private async Task<BlobMetadata> GetMetadataCoreAsync(string storagePath, CancellationToken cancellationToken)
    {
        var result = await _storage.GetBlobMetadataAsync(storagePath, cancellationToken).ConfigureAwait(false);
        return StorageResult.GetValue(result, $"metadata {storagePath}");
    }

    private async Task<Stream> OpenReadCoreAsync(string storagePath, CancellationToken cancellationToken)
    {
        var result = await _storage.GetStreamAsync(storagePath, cancellationToken).ConfigureAwait(false);
        return StorageResult.GetValue(result, $"read {storagePath}");
    }

}
