using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Rendering.Skia;

namespace ManagedCode.FileContext.Pdf;

/// <summary>Owns one parsed PDF for sequential page renders. Dispose it after the batch.</summary>
public sealed class FileContextPdfRenderDocument : IDisposable
{
    private readonly PdfDocument _document;
    private readonly FileContextOptions _options;
    private readonly Stream _source;
    private readonly bool _ownedSource;

    public FileContextPdfRenderDocument(byte[] pdf, FileContextOptions? options = null)
        : this(new MemoryStream(pdf ?? throw new ArgumentNullException(nameof(pdf)), writable: false), options)
    {
        _ownedSource = true;
    }

    /// <summary>Parses a seekable source without copying it. The caller owns the stream.</summary>
    public FileContextPdfRenderDocument(Stream pdf, FileContextOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        _options = (options ?? new FileContextOptions()).Clone();
        FileContextPdfSource.Validate(pdf, _options);
        _source = pdf;
        _document = PdfDocument.Open(pdf, SkiaRenderingParsingOptions.Instance);
        FileContextPdfPageFactory.Register(_document);
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
        var renderScale = (float)resolvedScale;
        var pageSize = _document.GetPageSize(pageNumber);
        var width = Math.Ceiling(pageSize.Width * renderScale);
        var height = Math.Ceiling(pageSize.Height * renderScale);
        if (width is not (> 0 and <= int.MaxValue) || height is not (> 0 and <= int.MaxValue))
        {
            throw new IOException("The rendered PDF page dimensions exceed the raster format's supported range.");
        }
        if (_options.MaximumRenderedPagePixels != int.MaxValue && width * height > _options.MaximumRenderedPagePixels)
        {
            throw new IOException("The rendered PDF page exceeds the pixel limit.");
        }
        FileContextPdfImageBudget.Validate(_document.GetPage(pageNumber), _options);
        using var bitmap = _document.GetPageAsSKBitmap(pageNumber, renderScale, SKColors.White);
        using var image = bitmap.Encode(SKEncodedImageFormat.Png, _options.PdfPngQuality)
            ?? throw new IOException("The rendered PDF page could not be encoded as PNG.");
        if (image.Size > _options.MaximumImageBytes)
        {
            throw new IOException("The rendered PDF page exceeds the image byte limit.");
        }
        return image.ToArray();
    }

    public void Dispose()
    {
        _document.Dispose();
        if (_ownedSource) { _source.Dispose(); }
    }
}
