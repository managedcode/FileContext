// Report geometry uses fixed typographic coordinates; block failures name their block index.
#pragma warning disable S109, MA0015
using System.Globalization;
using System.Text.Json;
using SkiaSharp;

namespace ManagedCode.FileContext;

internal sealed class FileContextReportPdf : IDisposable
{
    private readonly FileContextReport report;
    private readonly FileContextReportTheme theme;
    private readonly CancellationToken token;
    private readonly int pageLimit;
    private readonly SKDocument document;
    private readonly MemoryStream stream = new();
    private SKCanvas canvas = null!;
    private readonly float pageWidth;
    private readonly float pageHeight;
    private int pageNumber;
    internal const float Margin = 42;
    internal SKCanvas Canvas => canvas;
    internal float PageWidth => pageWidth;
    internal float PageHeight => pageHeight;
    internal float Bottom => pageHeight - FooterTop;
    internal float Y { get; set; }
    internal FileContextReportTheme Theme => theme;
    internal CancellationToken Token => token;
    private const float ContentTop = 93;
    private const float FooterTop = 52;

    private FileContextReportPdf(FileContextReport report, FileContextReportTheme theme, int pageLimit, CancellationToken token)
    {
        this.report = report;
        this.theme = theme;
        this.pageLimit = pageLimit;
        this.token = token;
        pageWidth = string.Equals(report.PageSize, "A4", StringComparison.Ordinal) ? 595 : 612;
        pageHeight = string.Equals(report.PageSize, "A4", StringComparison.Ordinal) ? 842 : 792;
        if (string.Equals(report.Orientation, "landscape", StringComparison.Ordinal)) { (pageWidth, pageHeight) = (pageHeight, pageWidth); }
        document = SKDocument.CreatePdf(stream) ?? throw new InvalidOperationException("PDF renderer could not start.");
    }

    public static byte[] Render(FileContextReport report, FileContextReportTheme theme, int pageLimit, CancellationToken token)
    {
        using var renderer = new FileContextReportPdf(report, theme, pageLimit, token);
        renderer.NewPage();
        for (var index = 0; index < report.Blocks.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            try { renderer.DrawBlock(report.Blocks[index]); }
            catch (Exception error) when (error is not OperationCanceledException)
            { throw new ArgumentException($"blocks[{index}] ({report.Blocks[index].Type}) failed: {error.Message}", error); }
        }
        renderer.document.EndPage();
        renderer.document.Close();
        return renderer.stream.ToArray();
    }

    internal void NewPage()
    {
        if (++pageNumber > pageLimit) { throw new ArgumentException($"Report exceeds {pageLimit} pages."); }
        if (pageNumber > 1) { document.EndPage(); }
        canvas = document.BeginPage(pageWidth, pageHeight);
        Y = ContentTop;
        using var primary = ReportCanvas.Paint(ReportCanvas.Parse(theme.Primary), 17);
        using var accent = ReportCanvas.Paint(ReportCanvas.Parse(theme.Accent));
        using var muted = ReportCanvas.Paint(ReportCanvas.Parse(theme.Muted), 9);
        if (theme.LogoBase64 is { Length: > 0 })
        {
            using var logo = SKBitmap.Decode(Convert.FromBase64String(theme.LogoBase64))
                ?? throw new ArgumentException("Theme logo must be PNG or JPEG.");
            canvas.DrawBitmap(logo, new SKRect(Margin, 26, Margin + 31, 57));
        }
        else
        {
            canvas.DrawRect(Margin, 26, 31, 31, accent);
            using var white = ReportCanvas.Paint(SKColors.White, 12);
            ReportCanvas.Text(canvas, "NY", Margin + 5, 47, white);
        }
        ReportCanvas.RequireFits(report.Title, primary, pageWidth - Margin * 2 - 43, "title");
        ReportCanvas.Text(canvas, report.Title, Margin + 43, 48, primary);
        canvas.DrawLine(Margin, 68, pageWidth - Margin, 68, accent);
        canvas.DrawLine(Margin, pageHeight - FooterTop, pageWidth - Margin, pageHeight - FooterTop, muted);
        ReportCanvas.Text(canvas, $"NYWD  •  {DateTime.UtcNow:yyyy-MM-dd}", Margin, pageHeight - 30, muted);
        ReportCanvas.Text(canvas, $"Page {pageNumber}", pageWidth - Margin - 44, pageHeight - 30, muted);
    }

    internal void Ensure(float height)
    {
        if (height > pageHeight - ContentTop - FooterTop) { throw new ArgumentException("A block is taller than one page."); }
        if (Y + height > pageHeight - FooterTop) { NewPage(); }
    }

    private void DrawBlock(FileContextReportBlock block)
    {
        switch (block.Type)
        {
            case "heading": DrawHeading(block.Content); break;
            case "text": DrawText(block.Content); break;
            case "kpi-row": ReportPdfKpiPainter.Draw(this, block.Content); break;
            case "chart": DrawChart(block.Content); break;
            case "table": ReportPdfTablePainter.Draw(this, block.Content); break;
            case "image": DrawImage(block.Content); break;
            case "callout": DrawCallout(block.Content); break;
            case "page-break": NewPage(); break;
        }
    }

    private void DrawHeading(JsonElement content)
    {
        using var title = ReportCanvas.Paint(ReportCanvas.Parse(theme.Primary), 22);
        using var subtitle = ReportCanvas.Paint(ReportCanvas.Parse(theme.Muted), 11);
        var titleLines = ReportCanvas.Wrap(ReportJson.Text(content, "title"), title, pageWidth - Margin * 2);
        var subtitleLines = ReportCanvas.Wrap(ReportJson.Text(content, "subtitle"), subtitle, pageWidth - Margin * 2);
        var height = titleLines.Count * 29 + subtitleLines.Count * 17 + 18;
        Ensure(height);
        foreach (var line in titleLines)
        {
            ReportCanvas.Text(canvas, line, Margin, Y + 22, title);
            Y += 29;
        }
        foreach (var line in subtitleLines)
        {
            ReportCanvas.Text(canvas, line, Margin, Y + 12, subtitle);
            Y += 17;
        }
        Y += 18;
    }

    private void DrawText(JsonElement content)
    {
        var source = ReportJson.Text(content, "text");
        source = System.Text.RegularExpressions.Regex.Replace(source,
            @"\[(?<label>[^\]]+)\]\((?<url>https://[^\s)]+)\)", "${label} (${url})",
            System.Text.RegularExpressions.RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));
        source = string.Join('\n', source.Split('\n').Select(line =>
            line.StartsWith("- ", StringComparison.Ordinal) ? "• " + line[2..] : line));
        using var paint = ReportCanvas.Paint(ReportCanvas.Parse(theme.Text), 10);
        var lines = ReportCanvas.Wrap(source, paint, pageWidth - Margin * 2);
        foreach (var line in lines)
        {
            Ensure(17);
            var x = Margin;
            var segments = line.Split("**", StringSplitOptions.None);
            for (var index = 0; index < segments.Length; index++)
            {
                using var segmentPaint = ReportCanvas.Paint(ReportCanvas.Parse(theme.Text), 10, bold: index % 2 == 1);
                ReportCanvas.Text(canvas, segments[index], x, Y + 12, segmentPaint);
                x += ReportCanvas.MeasureText(segments[index], segmentPaint);
            }
            Y += 17;
        }
        Y += 10;
    }

    private void DrawChart(JsonElement content)
    {
        Ensure(288);
        FileContextReportChart.Draw(canvas, new SKRect(Margin, Y, pageWidth - Margin, Y + 270), content, theme, token);
        Y += 288;
    }

    private void DrawImage(JsonElement content)
    {
        var bytes = Convert.FromBase64String(ReportJson.Text(content, "base64"));
        FileContextReportImage.Validate(bytes);
        using var image = SKBitmap.Decode(bytes) ?? throw new ArgumentException("image must be PNG or JPEG.");
        var height = Math.Min(260f, image.Height * (pageWidth - Margin * 2) / image.Width);
        using var caption = ReportCanvas.Paint(ReportCanvas.Parse(theme.Muted), 9);
        var lines = ReportCanvas.Wrap(ReportJson.Text(content, "caption"), caption, pageWidth - Margin * 2);
        Ensure(height + lines.Count * 14 + 12);
        canvas.DrawBitmap(image, new SKRect(Margin, Y, pageWidth - Margin, Y + height));
        Y += height + 5;
        foreach (var line in lines)
        {
            ReportCanvas.Text(canvas, line, Margin, Y + 10, caption);
            Y += 14;
        }
        Y += 7;
    }

    private void DrawCallout(JsonElement content)
    {
        using var paint = ReportCanvas.Paint(ReportCanvas.Parse(theme.Text), 10);
        var lines = ReportCanvas.Wrap(ReportJson.Text(content, "text"), paint, pageWidth - Margin * 2 - 26);
        var height = lines.Count * 16 + 22;
        Ensure(height + 10);
        using var fill = ReportCanvas.Paint(new SKColor(249, 242, 218));
        canvas.DrawRoundRect(new SKRect(Margin, Y, pageWidth - Margin, Y + height), 5, 5, fill);
        for (var index = 0; index < lines.Count; index++)
        { ReportCanvas.Text(canvas, lines[index], Margin + 13, Y + 18 + index * 16, paint); }
        Y += height + 10;
    }

    public void Dispose()
    {
        document.Dispose();
        stream.Dispose();
    }
}
