using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ManagedCode.FileContext;

internal static class FileContextDocxReader
{
    public static FileContextDocxText Read(WordprocessingDocument document, string path,
        int startParagraph, int startCharacter, int paragraphCount, int maximumCharacters, CancellationToken cancellationToken)
    {
        var main = document.MainDocumentPart
            ?? throw new InvalidDataException("The DOCX has no main document part.");
        using var reader = OpenXmlReader.Create(main);
        var paragraphs = new List<FileContextDocxParagraph>();
        var number = 0;
        var remaining = maximumCharacters == int.MaxValue ? long.MaxValue : maximumCharacters;
        reader.Read();
        while (MoveToParagraph(reader, cancellationToken))
        {
            number++;
            if (number < startParagraph)
            {
                // Read and skipped paragraphs must consume the same subtree, including text boxes.
                reader.Skip();
                continue;
            }
            if (paragraphs.Count == paragraphCount || remaining == 0)
            {
                return new FileContextDocxText(path, paragraphs, number, 0);
            }

            var paragraph = reader.LoadCurrentElement() as Paragraph
                ?? throw new InvalidDataException("A DOCX paragraph could not be read.");
            var offset = number == startParagraph ? startCharacter : 0;
            var (text, hasMore) = ReadParagraphWindow(paragraph, offset, remaining, cancellationToken);
            if (text.Length > 0 || !hasMore)
            {
                paragraphs.Add(new FileContextDocxParagraph(number, offset, text));
            }
            remaining -= text.Length;
            if (hasMore)
            {
                return new FileContextDocxText(path, paragraphs, number, offset + text.Length);
            }
            reader.Read();
        }

        return new FileContextDocxText(path, paragraphs, null, 0);
    }

    private static (string Text, bool HasMore) ReadParagraphWindow(
        Paragraph paragraph, int startCharacter, long limit, CancellationToken token)
    {
        var text = new StringBuilder();
        var skip = startCharacter;
        var lookahead = limit == long.MaxValue ? limit : limit + 1;
        var lastSkipped = '\0';
        foreach (var element in paragraph.Descendants())
        {
            token.ThrowIfCancellationRequested();
            var fragment = element switch
            {
                TabChar or PositionalTab => "\t",
                Break or CarriageReturn => "\n",
                { HasChildren: false } => element.InnerText,
                _ => string.Empty
            };

            var start = Math.Min(skip, fragment.Length);
            if (start > 0)
            {
                skip -= start;
                lastSkipped = fragment[start - 1];
            }
            if (start == fragment.Length) { continue; }
            if (text.Length == 0 && char.IsSurrogatePair(lastSkipped, fragment[start]))
            {
                throw InvalidOffset(nameof(startCharacter));
            }

            var length = (int)Math.Min(fragment.Length - start, lookahead - text.Length);
            text.Append(fragment, start, length);
            if (text.Length > limit)
            {
                var end = text.Length - 1;
                if (char.IsSurrogatePair(text[end - 1], text[end])) { end--; }
                return (text.ToString(0, end), true);
            }
        }

        if (skip > 0) { throw InvalidOffset(nameof(startCharacter)); }
        return (text.ToString(), false);
    }

    private static ArgumentOutOfRangeException InvalidOffset(string parameterName) =>
        new(parameterName, "The character offset must be within this paragraph and at a Unicode character boundary.");

    private static bool MoveToParagraph(OpenXmlReader reader, CancellationToken token)
    {
        while (!reader.EOF)
        {
            token.ThrowIfCancellationRequested();
            if (reader.IsStartElement && reader.ElementType == typeof(Paragraph))
            {
                return true;
            }
            reader.Read();
        }
        return false;
    }
}
