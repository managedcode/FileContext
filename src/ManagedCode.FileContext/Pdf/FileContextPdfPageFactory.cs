using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Outline.Destinations;
using UglyToad.PdfPig.Parser;
using UglyToad.PdfPig.Rendering.Skia;
using UglyToad.PdfPig.Tokenization.Scanner;
using UglyToad.PdfPig.Tokens;

namespace ManagedCode.FileContext.Pdf;

internal sealed class FileContextPdfPageFactory(
    IPdfTokenScanner scanner,
    IResourceStore resources,
    ILookupFilterProvider filters,
    IPageContentParser parser,
    ParsingOptions options) : IPageFactory<SKPicture>, IDisposable
{
    private static readonly Lazy<bool> HasDefaultFont = new(() =>
    {
        using var font = SKTypeface.Default.OpenStream();
        return font is not null;
    });

    private readonly SkiaPageFactory _factory = new(scanner, resources, filters, parser, options);
    private FileStream? _fallbackFont;

    public static void Register(PdfDocument document)
    {
        if (!OperatingSystem.IsLinux() || HasDefaultFont.Value)
        {
            document.AddSkiaPageFactory();
            return;
        }

        document.AddPageFactory<PdfPageSize, PageSizeFactory>();
        document.AddPageFactory<SKPicture, FileContextPdfPageFactory>();
    }

    public SKPicture Create(int number, DictionaryToken dictionary, PageTreeMembers pageTreeMembers,
        NamedDestinations namedDestinations)
    {
        _fallbackFont ??= StageFallbackFont();
        return _factory.Create(number, dictionary, pageTreeMembers, namedDestinations);
    }

    private FileStream StageFallbackFont()
    {
        // PdfPig's fallback API accepts a path. Keep it readable for the lifetime of its Skia typeface.
        var font = new FileStream(Path.Combine(Path.GetTempPath(), $"filecontext-font-{Guid.NewGuid():N}.ttf"),
            FileMode.CreateNew, FileAccess.Write, FileShare.Read, bufferSize: 4096, FileOptions.DeleteOnClose);
        try
        {
            using var bundledFont = BundledDocumentFont.Open();
            bundledFont.CopyTo(font);
            font.Flush();
            _factory.ReplaceDefaultFont(font.Name);
            return font;
        }
        catch
        {
            font.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        try { _factory.Dispose(); }
        finally { _fallbackFont?.Dispose(); }
    }
}
