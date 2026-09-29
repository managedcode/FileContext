using DocumentFormat.OpenXml.Packaging;

namespace ManagedCode.FileContext;

public sealed partial class FileContextDocumentService
{
    public Task<FileContextDocxText> ReadDocxTextAsync(string path, int startParagraph = 1,
        int startCharacter = 0, int paragraphCount = 20, CancellationToken cancellationToken = default)
    {
        RequireExtension(path, ".docx");
        ArgumentOutOfRangeException.ThrowIfLessThan(startParagraph, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(startCharacter);
        ArgumentOutOfRangeException.ThrowIfLessThan(paragraphCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(paragraphCount,
            FileContextDefaults.MaximumDocxParagraphsPerRead);

        return ReadSourceAsync(path, (buffer, token) =>
        {
            using var document = WordprocessingDocument.Open(buffer, false);
            return FileContextDocxReader.Read(document, path, startParagraph, startCharacter,
                paragraphCount, token);
        }, options.MaximumDocxReadBytes, cancellationToken);
    }
}
