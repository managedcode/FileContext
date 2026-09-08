using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace ManagedCode.FileContext;

internal static class FileContextPdfWriter
{
    private const double PageWidth = 595;
    private const double PageHeight = 842;
    private const double Margin = 50;
    private const double FontSize = 11;
    private const double LineHeight = 16;
    private const double ParagraphSpacing = 8;

    private static readonly Lazy<byte[]> Font = new(() =>
    {
        using var stream = typeof(FileContextPdfWriter).Assembly.GetManifestResourceStream(
            "ManagedCode.FileContext.Documents.Assets.NotoSans-Regular.ttf")
            ?? throw new InvalidOperationException("The bundled PDF font is missing.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    });

    public static byte[] Create(FileContextPdfDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Paragraphs is null) { throw new ArgumentException("Document members cannot be null.", nameof(document)); }
        var builder = new PdfDocumentBuilder();
        var font = builder.AddTrueTypeFont(Font.Value);
        var page = builder.AddPage(PageWidth, PageHeight);
        var y = PageHeight - Margin;
        var paragraphs = document.Title is null ? document.Paragraphs : new[] { document.Title }.Concat(document.Paragraphs);
        foreach (var paragraph in paragraphs)
        {
            if (paragraph is null) { throw new ArgumentException("Document members cannot be null.", nameof(document)); }
            foreach (var line in Wrap(paragraph, page, font))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (y < Margin) { page = builder.AddPage(PageWidth, PageHeight); y = PageHeight - Margin; }
                page.AddText(line, FontSize, new PdfPoint(Margin, y), font);
                y -= LineHeight;
            }
            y -= ParagraphSpacing;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return builder.Build();
    }

    private static IEnumerable<string> Wrap(string paragraph, PdfPageBuilder page, PdfDocumentBuilder.AddedFont font)
    {
        foreach (var source in paragraph.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var line = string.Empty;
            foreach (var rune in source.Replace("\t", "    ", StringComparison.Ordinal).EnumerateRunes())
            {
                var candidate = line + rune;
                var letters = page.MeasureText(candidate, FontSize, new PdfPoint(0, 0), font);
                if (letters.Count > 0 && letters[^1].BoundingBox.Right > PageWidth - Margin - Margin && line.Length > 0)
                {
                    yield return line;
                    line = rune.ToString();
                }
                else { line = candidate; }
            }
            yield return line;
        }
    }
}
