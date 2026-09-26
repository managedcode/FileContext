// KPI cards grow to fit their content; no model-authored value is truncated.
#pragma warning disable S109, MA0015
using System.Text.Json;
using SkiaSharp;

namespace ManagedCode.FileContext;

internal static class ReportPdfKpiPainter
{
    public static void Draw(FileContextReportPdf page, JsonElement content)
    {
        var items = ReportJson.Array(content, "items");
        if (items.Count is < 2 or > 6) { throw new ArgumentException("kpi-row.items requires 2 to 6 cards."); }
        var width = (page.PageWidth - FileContextReportPdf.Margin * 2 - (items.Count - 1) * 8) / items.Count;
        var cards = items.Select(item => Layout(item, width, page.Theme)).ToArray();
        var height = cards.Max(card => card.Height);
        page.Ensure(height + 14);
        for (var index = 0; index < cards.Length; index++)
        {
            var x = FileContextReportPdf.Margin + index * (width + 8);
            var card = cards[index];
            using var fill = ReportCanvas.Paint(new SKColor(240, 244, 248));
            page.Canvas.DrawRoundRect(new SKRect(x, page.Y, x + width, page.Y + height), 6, 6, fill);
            using var label = ReportCanvas.Paint(ReportCanvas.Parse(page.Theme.Muted), 9);
            using var value = ReportCanvas.Paint(ReportCanvas.Parse(page.Theme.Primary), 18);
            for (var line = 0; line < card.Label.Count; line++)
            { ReportCanvas.Text(page.Canvas, card.Label[line], x + 10, page.Y + 17 + line * 12, label); }
            var valueTop = page.Y + 24 + card.Label.Count * 12;
            for (var line = 0; line < card.Value.Count; line++)
            { ReportCanvas.Text(page.Canvas, card.Value[line], x + 10, valueTop + line * 21, value); }
            var trendY = valueTop + card.Value.Count * 21 + 5;
            DrawTrend(page.Canvas, x + 10, trendY, card.Indicator, card.Change, page.Theme);
        }
        page.Y += height + 14;
    }

    private static KpiLayout Layout(JsonElement item, float width, FileContextReportTheme theme)
    {
        using var label = ReportCanvas.Paint(ReportCanvas.Parse(theme.Muted), 9);
        using var value = ReportCanvas.Paint(ReportCanvas.Parse(theme.Primary), 18);
        var labelLines = ReportCanvas.Wrap(ReportJson.Text(item, "label"), label, width - 20);
        var valueLines = ReportCanvas.Wrap(ReportJson.Text(item, "value"), value, width - 20);
        var change = ReportJson.Text(item, "change");
        var indicator = ReportJson.Text(item, "indicator");
        using var trend = ReportCanvas.Paint(ReportCanvas.Parse(theme.Muted), 9);
        ReportCanvas.RequireFits(change, trend, width - (indicator is "up" or "down" ? 36 : 20), "kpi-row.items[].change");
        var height = Math.Max(76, 24 + labelLines.Count * 12 + valueLines.Count * 21 + 22);
        return new KpiLayout(labelLines, valueLines, change, indicator, height);
    }

    private static void DrawTrend(SKCanvas canvas, float x, float y, string indicator,
        string change, FileContextReportTheme theme)
    {
        var color = ReportCanvas.Parse(theme.Muted);
        if (indicator is "up") { color = new SKColor(33, 123, 82); }
        else if (indicator is "down") { color = new SKColor(177, 71, 64); }
        using var paint = ReportCanvas.Paint(color, 9);
        if (indicator is "up" or "down")
        {
            using var builder = new SKPathBuilder();
            if (indicator is "up")
            {
                builder.MoveTo(x, y + 8);
                builder.LineTo(x + 5, y);
                builder.LineTo(x + 10, y + 8);
            }
            else
            {
                builder.MoveTo(x, y);
                builder.LineTo(x + 5, y + 8);
                builder.LineTo(x + 10, y);
            }
            builder.Close();
            using var triangle = builder.Detach();
            canvas.DrawPath(triangle, paint);
            x += 16;
        }
        ReportCanvas.Text(canvas, change, x, y + 9, paint);
    }

    private sealed record KpiLayout(
        IReadOnlyList<string> Label, IReadOnlyList<string> Value,
        string Change, string Indicator, float Height);
}
