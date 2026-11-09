// Table row heights derive from wrapped text and remain intact across pages.
#pragma warning disable S109, MA0015
using System.Text.Json;
using SkiaSharp;

namespace ManagedCode.FileContext;

internal static class ReportPdfTablePainter
{
    public static void Draw(FileContextReportPdf page, JsonElement content)
    {
        var columns = ReportJson.Array(content, "columns");
        var rows = ReportJson.Array(content, "rows");
        if (columns.Count == 0) { throw new ArgumentException("table.columns is required."); }
        var width = (page.PageWidth - FileContextReportPdf.Margin * 2) / columns.Count;
        var totals = ReportJson.Property(content, "totals");
        var highlights = ReportJson.ReadHighlights(content);
        using var rowText = rows.Count > 0 ? ReportCanvas.Paint(ReportCanvas.Parse(page.Theme.Text), 9) : null;
        using var totalText = totals.ValueKind == JsonValueKind.Object
            ? ReportCanvas.Paint(ReportCanvas.Parse(page.Theme.Primary), 9) : null;
        DrawHeader(page, columns, width);
        for (var index = 0; index < rows.Count; index++)
        {
            page.Token.ThrowIfCancellationRequested();
            var cells = CellLines(rows[index], columns, width, rowText!);
            var height = RowHeight(cells);
            var totalsHeight = index == rows.Count - 1 && totalText is not null
                ? RowHeight(CellLines(totals, columns, width, totalText)) : 0;
            if (page.Y + height + totalsHeight > page.Bottom)
            {
                page.NewPage();
                DrawHeader(page, columns, width);
            }
            if (page.Y + height + totalsHeight > page.Bottom)
            { throw new ArgumentException($"table.rows[{index}] and its totals cannot fit on one page."); }
            using var fill = ReportCanvas.Paint(index % 2 == 0 ? new SKColor(250, 251, 253) : SKColors.White);
            page.Canvas.DrawRect(FileContextReportPdf.Margin, page.Y, page.PageWidth - FileContextReportPdf.Margin * 2, height, fill);
            for (var col = 0; col < columns.Count; col++)
            {
                if (highlights.Contains((index, ReportJson.Text(columns[col], "key"))))
                {
                    using var highlight = ReportCanvas.Paint(new SKColor(249, 242, 218));
                    page.Canvas.DrawRect(FileContextReportPdf.Margin + col * width, page.Y, width, height, highlight);
                }
            }
            DrawCells(page, cells, width, rowText!);
            page.Y += height;
        }
        if (totalText is not null)
        {
            DrawTotals(page, totals, columns, width, totalText);
        }
        page.Y += 12;
    }

    private static void DrawTotals(FileContextReportPdf page, JsonElement totals, IReadOnlyList<JsonElement> columns,
        float width, ReportPaint text)
    {
        var cells = CellLines(totals, columns, width, text);
        var height = RowHeight(cells);
        if (page.Y + height > page.Bottom)
        {
            page.NewPage();
            DrawHeader(page, columns, width);
        }

        page.Ensure(height);
        using var fill = ReportCanvas.Paint(new SKColor(230, 235, 241));
        page.Canvas.DrawRect(FileContextReportPdf.Margin, page.Y, page.PageWidth - FileContextReportPdf.Margin * 2, height, fill);
        DrawCells(page, cells, width, text);
        page.Y += height;
    }

    private static void DrawHeader(FileContextReportPdf page, IReadOnlyList<JsonElement> columns, float width)
    {
        using var paint = ReportCanvas.Paint(SKColors.White, 9);
        var cells = columns.Select(column => ReportCanvas.Wrap(
            ReportJson.Text(column, "label", ReportJson.Text(column, "key")), paint, width - 12)).ToArray();
        var height = RowHeight(cells);
        page.Ensure(height);
        using var fill = ReportCanvas.Paint(ReportCanvas.Parse(page.Theme.Primary));
        page.Canvas.DrawRect(FileContextReportPdf.Margin, page.Y, page.PageWidth - FileContextReportPdf.Margin * 2, height, fill);
        DrawCells(page, cells, width, paint);
        page.Y += height;
    }

    private static IReadOnlyList<string>[] CellLines(JsonElement row, IReadOnlyList<JsonElement> columns,
        float width, ReportPaint paint) =>
        columns.Select(column => ReportCanvas.Wrap(ReportJson.FormatCell(row, column), paint, width - 12)).ToArray();

    private static float RowHeight(IReadOnlyList<string>[] cells) =>
        Math.Max(28, cells.Max(lines => lines.Count) * 14 + 10);

    private static void DrawCells(FileContextReportPdf page, IReadOnlyList<string>[] cells, float width, ReportPaint text)
    {
        for (var col = 0; col < cells.Length; col++)
        {
            for (var line = 0; line < cells[col].Count; line++)
            {
                ReportCanvas.Text(page.Canvas, cells[col][line],
                    FileContextReportPdf.Margin + col * width + 6, page.Y + 17 + line * 14, text);
            }
        }
    }
}
