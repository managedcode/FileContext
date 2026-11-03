using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Vml = DocumentFormat.OpenXml.Vml;

namespace ManagedCode.FileContext.Tests;

internal static class FileContextDocxTestFiles
{
    public static byte[] WithParagraphs(params string[] texts)
        => WithBody(new Body(texts.Select(ParagraphWithText)));

    public static byte[] WithInlineSeparators()
    {
        var body = new Body();
        body.AppendChild(new Paragraph()).AppendChild(new Run(
            new Text("A"), new TabChar(), new Text("B"), new Break(), new Text("C"), new CarriageReturn(), new Text("D")));
        return WithBody(body);
    }

    public static byte[] WithTextBox()
    {
        var outer = ParagraphWithText("Outer ");
        var picture = outer.AppendChild(new Run()).AppendChild(new Picture());
        var box = picture.AppendChild(new Vml.Shape()).AppendChild(new Vml.TextBox());
        box.AppendChild(new TextBoxContent()).AppendChild(ParagraphWithText("boxed"));
        return WithBody(new Body(outer, ParagraphWithText("Second"), ParagraphWithText("Third")));
    }

    private static byte[] WithBody(Body body)
    {
        using var stream = new MemoryStream();
        using (var package = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = package.AddMainDocumentPart();
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
