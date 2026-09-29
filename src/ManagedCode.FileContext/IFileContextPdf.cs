using ManagedCode.FileContext.Pdf;
using Microsoft.Extensions.AI;

namespace ManagedCode.FileContext;

/// <summary>Optional bounded PDF inspection and vision content over scoped storage.</summary>
public interface IFileContextPdf
{
    Task<FileContextPdfText> ReadPdfTextAsync(string path, int maxCharacters = FileContextDefaults.MaximumPdfTextCharacters, CancellationToken cancellationToken = default);

    Task<DataContent> RenderPdfPageAsync(string path, int pageNumber, CancellationToken cancellationToken = default);

    Task<DataContent> ExtractPdfImageAsync(string path, int pageNumber, int imageNumber, CancellationToken cancellationToken = default);

    Task<int> CountPdfPageImagesAsync(string path, int pageNumber, CancellationToken cancellationToken = default);

}
