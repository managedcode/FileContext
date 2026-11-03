using DocumentFormat.OpenXml.Packaging;

namespace ManagedCode.FileContext;

public sealed partial class FileContextDocumentService
{
    public Task<FileContextDocxText> ReadDocxTextAsync(string path, int startParagraph = 1,
        int startCharacter = 0, int paragraphCount = FileContextDefaults.MaximumDocxParagraphsPerRead, CancellationToken cancellationToken = default)
    {
        RequireExtension(path, ".docx");
        ArgumentOutOfRangeException.ThrowIfLessThan(startParagraph, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(startCharacter);
        ArgumentOutOfRangeException.ThrowIfLessThan(paragraphCount, 1);

        return ReadSourceAsync(path, (buffer, token) =>
        {
            using var document = WordprocessingDocument.Open(buffer, false,
                new OpenSettings { MaxCharactersInPart = options.MaximumOpenXmlPartCharacters });
            return FileContextDocxReader.Read(document, path, startParagraph, startCharacter,
                paragraphCount, options.MaximumDocxTextCharacters, token);
        }, options.DocxSourceReadBudget, cancellationToken);
    }
}
