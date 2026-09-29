using UglyToad.PdfPig;
using UglyToad.PdfPig.Rendering.Skia;

namespace ManagedCode.FileContext.Pdf;

/// <summary>Renders complete PDF pages and extracts embedded page images as PNG.</summary>
public static class FileContextPdfImages
{
    public const int MaximumPdfBytes = 100 * 1024 * 1024;
    public const int MaximumImageBytes = 8 * 1024 * 1024;
    public const int MaximumPixels = 4_000_000;
    public const int MaximumImagesPerPage = 20;
    public const double DefaultScale = 1.5;
    public const double MinimumScale = 0.5;
    public const double MaximumScale = 3;
    public const int PngQuality = 100;

    /// <summary>Renders one complete one-based page to PNG bytes, including text and vector graphics.</summary>
    public static byte[] RenderPagePng(byte[] pdf, int pageNumber, double scale = DefaultScale)
    {
        ValidateInput(pdf);
        if (scale is < MinimumScale or > MaximumScale || !double.IsFinite(scale))
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        using var document = PdfDocument.Open(pdf, SkiaRenderingParsingOptions.Instance);
        ValidatePage(pageNumber, document.NumberOfPages);
        var page = document.GetPage(pageNumber);
        if (page.Width * scale * page.Height * scale > MaximumPixels)
        {
            throw new IOException("The rendered PDF page exceeds the pixel limit.");
        }

        document.AddSkiaPageFactory();
        using var image = document.GetPageAsPng(pageNumber, (float)scale, PngQuality);
        if (image.Length > MaximumImageBytes)
        {
            throw new IOException("The rendered PDF page exceeds the image byte limit.");
        }
        return image.ToArray();
    }

    /// <summary>Extracts the embedded images on one page; these do not include page text or vector drawings.</summary>
    public static IReadOnlyList<FileContextPdfImage> ExtractPageImagesPng(byte[] pdf, int pageNumber)
    {
        ValidateInput(pdf);
        using var document = PdfDocument.Open(pdf, SkiaRenderingParsingOptions.Instance);
        ValidatePage(pageNumber, document.NumberOfPages);
        var result = new List<FileContextPdfImage>();
        foreach (var image in document.GetPage(pageNumber).GetImages())
        {
            if (result.Count >= MaximumImagesPerPage)
            {
                throw new IOException("The PDF page exceeds the embedded image count limit.");
            }
            if (!image.TryGetPng(out var bytes))
            {
                throw new NotSupportedException("An embedded PDF image could not be decoded as PNG.");
            }
            if (bytes.Length > MaximumImageBytes)
            {
                throw new IOException("An embedded PDF image exceeds the image byte limit.");
            }
            result.Add(new FileContextPdfImage(pageNumber, result.Count + 1, bytes.ToArray()));
        }
        return result;
    }

    private static void ValidateInput(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        if (pdf.Length > MaximumPdfBytes)
        {
            throw new IOException("The PDF exceeds the read limit.");
        }
    }

    private static void ValidatePage(int pageNumber, int pageCount)
    {
        if (pageNumber < 1 || pageNumber > pageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber));
        }
    }
}
