using UglyToad.PdfPig;
using UglyToad.PdfPig.Rendering.Skia;

namespace ManagedCode.FileContext.Pdf;

/// <summary>Owns one parsed PDF for sequential page renders. Dispose it after the batch.</summary>
public sealed class FileContextPdfRenderDocument : IDisposable
{
    private readonly PdfDocument _document;
    private readonly FileContextOptions _options;

    public FileContextPdfRenderDocument(byte[] pdf, FileContextOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        _options = (options ?? new FileContextOptions()).Clone();
        _options.Validate();
        if (pdf.Length > _options.MaximumPdfReadBytes)
        {
            throw new IOException("The PDF exceeds the read limit.");
        }
        _document = PdfDocument.Open(pdf, SkiaRenderingParsingOptions.Instance);
        _document.AddSkiaPageFactory();
    }

    public int PageCount => _document.NumberOfPages;

    public byte[] RenderPagePng(int pageNumber, double? scale = null)
    {
        if (pageNumber < 1 || pageNumber > PageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber));
        }
        var resolvedScale = scale ?? _options.DefaultPdfPageScale;
        if (!double.IsFinite(resolvedScale) || resolvedScale < _options.MinimumPdfPageScale
            || resolvedScale > _options.MaximumPdfPageScale)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }
        var page = _document.GetPage(pageNumber);
        if (page.Width * resolvedScale * page.Height * resolvedScale > _options.MaximumRenderedPagePixels)
        {
            throw new IOException("The rendered PDF page exceeds the pixel limit.");
        }
        using var image = _document.GetPageAsPng(pageNumber, (float)resolvedScale, _options.PdfPngQuality);
        if (image.Length > _options.MaximumImageBytes)
        {
            throw new IOException("The rendered PDF page exceeds the image byte limit.");
        }
        return image.ToArray();
    }

    public void Dispose() => _document.Dispose();
}
