namespace ManagedCode.FileContext;

public sealed partial class FileContextService
{
    public Task<IReadOnlyList<FileContextInfo>> ListFilesAsync(string directory = "", CancellationToken cancellationToken = default) =>
        FileContextOperation.RunAsync(_options.OperationTimeout, token => ListFilesOperationAsync(directory, token), cancellationToken);

    private async Task<IReadOnlyList<FileContextInfo>> ListFilesOperationAsync(string directory, CancellationToken cancellationToken)
    {
        var normalized = StoragePathScope.Normalize(directory, allowEmpty: true);
        var files = new List<FileContextInfo>();
        await foreach (var (path, metadata) in _fileStore.EnumerateScopedFilesAsync(normalized, cancellationToken).ConfigureAwait(false))
        {
            files.Add(new FileContextInfo(path, metadata.Length, metadata.MimeType, metadata.LastModified));
            if (files.Count >= _options.MaximumSearchFiles) { break; }
        }
        return files;
    }
}
