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

    public override Task WriteAsync(string path, string content, CancellationToken cancellationToken = default)
        => FileContextOperation.RunAsync(_options.OperationTimeout, token => WriteOperationAsync(path, content, token), cancellationToken);

    public override Task<string?> ReadAsync(string path, CancellationToken cancellationToken = default)
        => FileContextOperation.RunAsync(_options.OperationTimeout, token => ReadOperationAsync(path, token), cancellationToken);

    public override Task<bool> DeleteAsync(string path, CancellationToken cancellationToken = default)
        => FileContextOperation.RunAsync(_options.OperationTimeout, token => DeleteOperationAsync(path, token), cancellationToken);

    public override Task<IReadOnlyList<FileStoreEntry>> ListChildrenAsync(string directory, CancellationToken cancellationToken = default)
        => FileContextOperation.RunAsync(_options.OperationTimeout, token => ListChildrenOperationAsync(directory, token), cancellationToken);

    public override Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default)
        => FileContextOperation.RunAsync(_options.OperationTimeout, token => FileExistsOperationAsync(path, token), cancellationToken);

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
        if (!await ExistsCoreAsync(storagePath, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var metadata = await GetMetadataCoreAsync(storagePath, cancellationToken).ConfigureAwait(false);
        if (metadata.Length > (ulong)_options.MaximumFullReadBytes)
        {
            throw new IOException($"File '{path}' is {metadata.Length} bytes; the full-read limit is {_options.MaximumFullReadBytes} bytes. Use file_context_read_range.");
        }

        var stream = await OpenReadCoreAsync(storagePath, cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            return await StorageTextReader.ReadAsync(stream, _options.MaximumFullReadBytes, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> DeleteOperationAsync(string path, CancellationToken cancellationToken = default)
    {
        var storagePath = _paths.ToStoragePath(path);
        var result = await _storage.DeleteAsync(storagePath, cancellationToken).ConfigureAwait(false);
        StorageResult.EnsureSuccess(result, $"delete {path}");
        return result.Value;
    }

    private async Task<IReadOnlyList<FileStoreEntry>> ListChildrenOperationAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        var normalizedDirectory = StoragePathScope.Normalize(directory, allowEmpty: true);
        var storageDirectory = _paths.ToStoragePath(normalizedDirectory, allowEmpty: true);
        var children = new Dictionary<string, string>(StringComparer.Ordinal);

        await foreach (var metadata in _storage.GetBlobMetadataListAsync(storageDirectory, cancellationToken).ConfigureAwait(false))
        {
            var scopedPath = _paths.FromStoragePath(metadata.FullName);
            if (scopedPath is null || !StoragePathScope.TryGetRemainder(scopedPath, normalizedDirectory, out var remainder))
            {
                continue;
            }

            var separatorIndex = remainder.IndexOf('/', StringComparison.Ordinal);
            var name = separatorIndex < 0 ? remainder : remainder[..separatorIndex];
            if (separatorIndex >= 0 || !children.ContainsKey(name))
            {
                children[name] = separatorIndex < 0 ? FileStoreEntry.File : FileStoreEntry.Directory;
            }
        }

        return children
            .OrderBy(static pair => pair.Value, StringComparer.Ordinal)
            .ThenBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => new FileStoreEntry(pair.Key, pair.Value))
            .ToArray();
    }

    private Task<bool> FileExistsOperationAsync(string path, CancellationToken cancellationToken = default)
    {
        return ExistsCoreAsync(_paths.ToStoragePath(path), cancellationToken);
    }

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

    internal async Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        return await OpenReadCoreAsync(_paths.ToStoragePath(path), cancellationToken).ConfigureAwait(false);
    }

    internal async Task<BlobMetadata?> GetMetadataAsync(string path, CancellationToken cancellationToken)
    {
        var storagePath = _paths.ToStoragePath(path);
        return await ExistsCoreAsync(storagePath, cancellationToken).ConfigureAwait(false)
            ? await GetMetadataCoreAsync(storagePath, cancellationToken).ConfigureAwait(false)
            : null;
    }

    internal async IAsyncEnumerable<BlobMetadata> EnumerateScopedFilesAsync(
        string directory,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var storageDirectory = _paths.ToStoragePath(directory, allowEmpty: true);
        await foreach (var metadata in _storage.GetBlobMetadataListAsync(storageDirectory, cancellationToken).ConfigureAwait(false))
        {
            if (_paths.FromStoragePath(metadata.FullName) is not null)
            {
                yield return metadata;
            }
        }
    }

    internal string ToScopedPath(string storagePath) => _paths.FromStoragePath(storagePath)
        ?? throw new InvalidOperationException("The storage item is outside the configured root.");

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
