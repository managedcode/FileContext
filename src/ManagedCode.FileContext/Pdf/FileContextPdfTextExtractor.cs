using System.Globalization;
using System.Text;

using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;

namespace ManagedCode.FileContext.Pdf;

/// <summary>
///     Reads the text layer of a PDF memory, page by page, in content order. There is no OCR: a scanned page has no
///     text layer and is reported in <see cref="FileContextPdfText.PagesWithoutText"/>. A PDF that cannot be opened,
///     or a page that cannot be read, becomes an error message rather than an exception. Only a bounded prefix of the
///     text is kept, however large the text layer is; every page is still visited for the page metadata.
/// </summary>
public static class FileContextPdfTextExtractor
{
    /// <summary>A page with fewer words than this, and fewer letters or digits than
    /// <see cref="MinLettersForTextPage"/>, is treated as having no text layer (a scan, or a bare page number).</summary>
    public const int MinWordsForTextPage = 5;

    /// <summary>
    ///     Lets a page in a script written without spaces between words (Chinese, Japanese, Thai) count as text
    ///     even though whitespace splitting sees it as one word.
    /// </summary>
    public const int MinLettersForTextPage = 20;

    public const string EncryptedMessage =
        "The PDF is password-protected, so its text could not be read.";

    public const string UnreadableMessage =
        "The PDF could not be opened (it may be damaged or not a real PDF), so its text could not be read.";

    public const string UnreadablePagesMessagePrefix = "The text of these pages could not be read: ";

    private const string PageSeparator = "\n\n";
    private const string PageListSeparator = ", ";
    private const string SentenceEnd = ".";

    /// <summary>Extracts the text layer, keeping at most <paramref name="maxRetainedChars"/> characters of it.</summary>
    public static FileContextPdfText Extract(byte[] pdf, int maxRetainedChars)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        using var source = new MemoryStream(pdf, writable: false);
        return Extract(source, maxRetainedChars);
    }

    /// <summary>Extracts bounded text from a seekable source without copying the PDF. The caller owns the stream.</summary>
    public static FileContextPdfText Extract(Stream pdf, int maxRetainedChars, FileContextOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetainedChars);
        FileContextPdfSource.Validate(pdf, options ?? new FileContextOptions());
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(pdf);
        }
        catch (PdfDocumentEncryptedException exception)
        {
            return new FileContextPdfText(null, string.Empty, false, [], EncryptedMessage, exception);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new FileContextPdfText(null, string.Empty, false, [], UnreadableMessage, exception);
        }

        using (document)
        {
            return ReadPages(document, maxRetainedChars);
        }
    }

    private static FileContextPdfText ReadPages(PdfDocument document, int maxRetainedChars)
    {
        var pageCount = document.NumberOfPages;
        var text = new StringBuilder();
        var cut = false;
        var withoutText = new List<int>();
        var unreadable = new List<int>();
        Exception? failure = null;

        for (var number = 1; number <= pageCount; number++)
        {
            FileContextPdfPageText? page;
            Exception? pageFailure;
            try
            {
                page = FileContextPdfPageReader.Read(document.GetPage(number), maxRetainedChars);
                pageFailure = null;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                page = null;
                pageFailure = exception;
            }

            if (page is null)
            {
                failure ??= pageFailure;
                unreadable.Add(number);
                withoutText.Add(number);
                continue;
            }

            if (!page.HasTextLayer)
            {
                withoutText.Add(number);
            }

            var appendCut = AppendBounded(text, page.Text, maxRetainedChars);
            cut |= page.TextCut || appendCut;
        }

        return new FileContextPdfText(
            pageCount, text.ToString(), cut, withoutText, DescribeUnreadable(unreadable), failure);
    }

    /// <summary>Appends a page within the budget; true when part of it (or of the separator) had to be dropped.</summary>
    private static bool AppendBounded(StringBuilder text, string pageText, int maxRetainedChars)
    {
        if (pageText.Length == 0)
        {
            return false;
        }

        var addition = text.Length == 0 ? pageText : PageSeparator + pageText;
        var room = Math.Max(0, maxRetainedChars - text.Length);
        text.Append(addition, 0, Math.Min(room, addition.Length));
        return addition.Length > room;
    }

    internal static bool HasTextLayer(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length >= MinWordsForTextPage
        || text.Count(char.IsLetterOrDigit) >= MinLettersForTextPage;

    private static string? DescribeUnreadable(List<int> pages) =>
        pages.Count == 0
            ? null
            : UnreadablePagesMessagePrefix
              + string.Join(PageListSeparator, pages.Select(page => page.ToString(CultureInfo.InvariantCulture)))
              + SentenceEnd;

}
