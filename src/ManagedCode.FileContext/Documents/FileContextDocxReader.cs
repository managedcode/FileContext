using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ManagedCode.FileContext;

internal static class FileContextDocxReader
{
    public static FileContextDocxText Read(WordprocessingDocument document, string path,
        int startParagraph, int startCharacter, int paragraphCount, CancellationToken cancellationToken)
    {
        var main = document.MainDocumentPart
            ?? throw new InvalidDataException("The DOCX has no main document part.");
        using var reader = OpenXmlReader.Create(main);
        var paragraphs = new List<FileContextDocxParagraph>();
        var number = 0;
        var remaining = FileContextDefaults.MaximumDocxTextCharacters;
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!reader.IsStartElement || reader.ElementType != typeof(Paragraph))
            {
                continue;
            }

            number++;
            if (number < startParagraph)
            {
                continue;
            }
            if (paragraphs.Count == paragraphCount || remaining == 0)
            {
                return new FileContextDocxText(path, paragraphs, number, 0);
            }

            var paragraph = reader.LoadCurrentElement() as Paragraph
                ?? throw new InvalidDataException("A DOCX paragraph could not be read.");
            var text = paragraph.InnerText;
            var offset = number == startParagraph ? startCharacter : 0;
            if (offset > text.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(startCharacter),
                    "The character offset is beyond this paragraph.");
            }
            var length = Math.Min(text.Length - offset, remaining);
            paragraphs.Add(new FileContextDocxParagraph(number, offset, text.Substring(offset, length)));
            remaining -= length;
            if (offset + length < text.Length)
            {
                return new FileContextDocxText(path, paragraphs, number, offset + length);
            }
        }

        return new FileContextDocxText(path, paragraphs, null, 0);
    }
}
