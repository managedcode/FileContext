namespace ManagedCode.FileContext;

public sealed partial class FileContextDocumentService
{
    public Task<FileContextTablesInfo> GetTablesInfoAsync(string path, int? headerRow = null,
        string? delimiter = null, CancellationToken cancellationToken = default)
    {
        FileContextTableReader.Validate(path, headerRow);
        return ReadSourceAsync(path,
            (buffer, token) => FileContextTableReader.Read(buffer, path, headerRow, delimiter, options, token), cancellationToken);
    }
}
