using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ManagedCode.FileContext.Tests;

internal static class FileContextDocxTestFiles
{
    public static byte[] WithParagraphs(params string[] texts)
    {
        using var stream = new MemoryStream();
        using (var package = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = package.AddMainDocumentPart();
            var body = new Body();
            foreach (var text in texts)
            {
                body.AppendChild(ParagraphWithText(text));
            }
            var document = new Document();
            document.AppendChild(body);
            main.Document = document;
            main.Document.Save();
        }
        return stream.ToArray();
    }

    public static byte[] WithTable()
    {
        using var stream = new MemoryStream();
        using (var package = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = package.AddMainDocumentPart();
            var document = new Document();
            var body = document.AppendChild(new Body());
            body.AppendChild(ParagraphWithText("Before table"));
            var table = body.AppendChild(new Table());
            var row = table.AppendChild(new TableRow());
            var cell = row.AppendChild(new TableCell());
            cell.AppendChild(ParagraphWithText("Cell value"));
            body.AppendChild(ParagraphWithText("After table"));
            main.Document = document;
            main.Document.Save();
        }
        return stream.ToArray();
    }

    private static Paragraph ParagraphWithText(string text)
    {
        var paragraph = new Paragraph();
        paragraph.AppendChild(new Run()).AppendChild(new Text(text));
        return paragraph;
    }
}
