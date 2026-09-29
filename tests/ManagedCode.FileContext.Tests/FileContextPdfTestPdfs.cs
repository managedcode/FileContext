using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace ManagedCode.FileContext.Tests;

/// <summary>Builds small PDFs in memory, so the PDF tests need no binary fixtures.</summary>
internal static class FileContextPdfTestPdfs
{
    private const double FontSize = 11;
    private const double Left = 40;
    private const double Top = 780;
    private const double LineHeight = 16;

    /// <summary>A 1×1 RGBA PNG, standing in for a scanned page image.</summary>
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    /// <summary>One page per entry; each entry is that page's lines, and an empty entry is a page with no text.</summary>
    public static byte[] WithPages(params string[][] pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var lines in pages)
        {
            var page = builder.AddPage(PageSize.A4);
            for (var index = 0; index < lines.Length; index++)
            {
                page.AddText(lines[index], FontSize, new PdfPoint(Left, Top - (index * LineHeight)), font);
            }
        }

        return builder.Build();
    }

    public static byte[] WithEmbeddedImages(int imageCount)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        for (var index = 0; index < imageCount; index++)
        {
            page.AddPng(OnePixelPng, new PdfRectangle(Left + index, Left, Top, Top));
        }
        return builder.Build();
    }

    /// <summary>A scan: every page is only an image, with no text layer.</summary>
    public static byte[] ScannedPages(int pageCount)
    {
        var builder = new PdfDocumentBuilder();
        for (var index = 0; index < pageCount; index++)
        {
            var page = builder.AddPage(PageSize.A4);
            page.AddPng(OnePixelPng, new PdfRectangle(Left, Left, Top, Top));
        }

        return builder.Build();
    }
}
