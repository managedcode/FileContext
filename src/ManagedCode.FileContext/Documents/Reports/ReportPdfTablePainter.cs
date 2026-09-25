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
        DrawHeader(page, columns, width);
        for (var index = 0; index < rows.Count; index++)
        {
            page.Token.ThrowIfCancellationRequested();
            var cells = CellLines(rows[index], columns, width, page.Theme.Text);
            var height = RowHeight(cells);
            var totalsHeight = index == rows.Count - 1 && totals.ValueKind == JsonValueKind.Object
                ? RowHeight(CellLines(totals, columns, width, page.Theme.Primary)) : 0;
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
                if (ReportJson.IsHighlighted(content, index, ReportJson.Text(columns[col], "key")))
                {
                    using var highlight = ReportCanvas.Paint(new SKColor(249, 242, 218));
                    page.Canvas.DrawRect(FileContextReportPdf.Margin + col * width, page.Y, width, height, highlight);
                }
            }
            DrawCells(page, cells, width, page.Theme.Text);
            page.Y += height;
        }
        if (totals.ValueKind == JsonValueKind.Object)
        {
            var cells = CellLines(totals, columns, width, page.Theme.Primary);
            var height = RowHeight(cells);
            if (page.Y + height > page.Bottom)
            {
                page.NewPage();
                DrawHeader(page, columns, width);
            }
            page.Ensure(height);
            using var fill = ReportCanvas.Paint(new SKColor(230, 235, 241));
            page.Canvas.DrawRect(FileContextReportPdf.Margin, page.Y, page.PageWidth - FileContextReportPdf.Margin * 2, height, fill);
            DrawCells(page, cells, width, page.Theme.Primary);
            page.Y += height;
        }
        page.Y += 12;
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
        DrawCells(page, cells, width, "#FFFFFF");
        page.Y += height;
    }

    private static IReadOnlyList<string>[] CellLines(JsonElement row, IReadOnlyList<JsonElement> columns,
        float width, string color)
    {
        using var paint = ReportCanvas.Paint(ReportCanvas.Parse(color), 9);
        return columns.Select(column => ReportCanvas.Wrap(ReportJson.FormatCell(row, column), paint, width - 12)).ToArray();
    }

    private static float RowHeight(IReadOnlyList<string>[] cells) =>
        Math.Max(28, cells.Max(lines => lines.Count) * 14 + 10);

    private static void DrawCells(FileContextReportPdf page, IReadOnlyList<string>[] cells, float width, string color)
    {
        using var text = ReportCanvas.Paint(ReportCanvas.Parse(color), 9);
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
