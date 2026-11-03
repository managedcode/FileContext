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
            var text = ReadParagraphText(paragraph, cancellationToken);
            var offset = number == startParagraph ? startCharacter : 0;
            if (offset > text.Length || SplitsSurrogatePair(text, offset))
            {
                throw new ArgumentOutOfRangeException(nameof(startCharacter),
                    "The character offset must be within this paragraph and at a Unicode character boundary.");
            }
            var length = (int)Math.Min(text.Length - offset, remaining);
            if (SplitsSurrogatePair(text, offset + length)) { length--; }
            if (length > 0 || offset == text.Length)
            {
                paragraphs.Add(new FileContextDocxParagraph(number, offset, text.Substring(offset, length)));
            }
            remaining -= length;
            if (offset + length < text.Length)
            {
                return new FileContextDocxText(path, paragraphs, number, offset + length);
            }
            reader.Read();
        }

        return new FileContextDocxText(path, paragraphs, null, 0);
    }

    private static string ReadParagraphText(Paragraph paragraph, CancellationToken token)
    {
        var text = new StringBuilder();
        foreach (var element in paragraph.Descendants())
        {
            token.ThrowIfCancellationRequested();
            switch (element)
            {
                case TabChar or PositionalTab: text.Append('\t'); break;
                case Break or CarriageReturn: text.Append('\n'); break;
                case { HasChildren: false }: text.Append(element.InnerText); break;
            }
        }
        return text.ToString();
    }

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

    private static bool SplitsSurrogatePair(string text, int index) =>
        index > 0 && index < text.Length && char.IsSurrogatePair(text, index - 1);
}
