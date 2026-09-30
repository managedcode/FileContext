using UglyToad.PdfPig;
using UglyToad.PdfPig.Rendering.Skia;

namespace ManagedCode.FileContext.Pdf;

/// <summary>Renders complete PDF pages and extracts embedded page images as PNG.</summary>
public static class FileContextPdfImages
{
    public const int MaximumPdfBytes = FileContextDefaults.MaximumPdfReadBytes;
    public const int MaximumImageBytes = FileContextDefaults.MaximumImageBytes;
    public const int MaximumPixels = FileContextDefaults.MaximumRenderedPagePixels;
    public const int MaximumImagesPerPage = FileContextDefaults.MaximumImagesPerPdfPage;
    public const double DefaultScale = FileContextDefaults.DefaultPdfPageScale;
    public const double MinimumScale = FileContextDefaults.MinimumPdfPageScale;
    public const double MaximumScale = FileContextDefaults.MaximumPdfPageScale;
    public const int PngQuality = FileContextDefaults.PdfPngQuality;

    /// <summary>Renders one complete one-based page to PNG bytes, including text and vector graphics.</summary>
    public static byte[] RenderPagePng(byte[] pdf, int pageNumber, double scale = DefaultScale) =>
        RenderPagePng(pdf, pageNumber, new FileContextOptions(), scale);

    /// <summary>Renders a page using the configured source, scale, pixel, and image limits.</summary>
    public static byte[] RenderPagePng(byte[] pdf, int pageNumber, FileContextOptions options,
        double? scale = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        using var document = new FileContextPdfRenderDocument(pdf, options);
        return document.RenderPagePng(pageNumber, scale);
    }

    /// <summary>Extracts the embedded images on one page; these do not include page text or vector drawings.</summary>
    public static IReadOnlyList<FileContextPdfImage> ExtractPageImagesPng(byte[] pdf, int pageNumber) =>
        ExtractPageImagesPng(pdf, pageNumber, new FileContextOptions());

    /// <summary>Extracts embedded images using the configured source, count, and image limits.</summary>
    public static IReadOnlyList<FileContextPdfImage> ExtractPageImagesPng(byte[] pdf, int pageNumber,
        FileContextOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var settings = ValidateInput(pdf, options);
        using var document = PdfDocument.Open(pdf, SkiaRenderingParsingOptions.Instance);
        ValidatePage(pageNumber, document.NumberOfPages);
        var result = new List<FileContextPdfImage>();
        foreach (var image in document.GetPage(pageNumber).GetImages())
        {
            if (result.Count >= settings.MaximumImagesPerPdfPage)
            {
                throw new IOException("The PDF page exceeds the embedded image count limit.");
            }
            if (!image.TryGetPng(out var bytes))
            {
                throw new NotSupportedException("An embedded PDF image could not be decoded as PNG.");
            }
            if (bytes.Length > settings.MaximumImageBytes)
            {
                throw new IOException("An embedded PDF image exceeds the image byte limit.");
            }
            result.Add(new FileContextPdfImage(pageNumber, result.Count + 1, bytes.ToArray()));
        }
        return result;
    }

    private static FileContextOptions ValidateInput(byte[] pdf, FileContextOptions options)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        options.Validate();
        if (pdf.Length > options.MaximumPdfReadBytes)
        {
            throw new IOException("The PDF exceeds the read limit.");
        }
        return options;
    }

    private static void ValidatePage(int pageNumber, int pageCount)
    {
        if (pageNumber < 1 || pageNumber > pageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber));
        }
    }
}
