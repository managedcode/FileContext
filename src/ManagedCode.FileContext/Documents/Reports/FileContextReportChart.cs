// Report geometry uses fixed typographic coordinates; block failures name their block index.
#pragma warning disable S109, MA0015
using System.Text.Json;
using SkiaSharp;

namespace ManagedCode.FileContext;

internal static class FileContextReportChart
{
    private static readonly SKColor[] Palette =
    [new(31, 91, 143), new(211, 167, 71), new(54, 133, 109), new(138, 87, 155), new(212, 104, 90), new(75, 125, 165)];

    public static byte[] RenderPng(JsonElement content, FileContextReportTheme theme, CancellationToken token)
    {
        using var surface = SKSurface.Create(new SKImageInfo(1200, 650))
            ?? throw new InvalidOperationException("Could not create chart surface.");
        Draw(surface.Canvas, new SKRect(0, 0, 1200, 650), content, theme, token);
        using var image = surface.Snapshot();
        using var bytes = image.Encode(SKEncodedImageFormat.Png, 95);
        return bytes.ToArray();
    }

    public static void Draw(SKCanvas canvas, SKRect bounds, JsonElement content,
        FileContextReportTheme theme, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var chartType = ReportJson.Text(content, "chartType", "bar").ToLowerInvariant();
        if (chartType is not ("line" or "bar" or "stacked-bar" or "pie" or "donut"))
        { throw new ArgumentException($"chart.chartType '{chartType}' is unsupported."); }
        var labels = ReportJson.Strings(content, "labels");
        var series = ParseSeries(content, labels.Count);
        if (labels.Count == 0 || series.Count == 0) { throw new ArgumentException("chart requires labels and series values."); }
        using var background = ReportCanvas.Paint(ReportCanvas.Parse(theme.Background));
        canvas.DrawRect(bounds, background);
        using var titlePaint = ReportCanvas.Paint(ReportCanvas.Parse(theme.Primary), 20);
        var title = ReportJson.Text(content, "title", "Chart");
        ReportCanvas.RequireFits(title, titlePaint, bounds.Width - 56, "chart.title");
        ReportCanvas.Text(canvas, title, bounds.Left + 28, bounds.Top + 38, titlePaint);
        if (chartType is "pie" or "donut") { DrawPie(canvas, bounds, labels, series[0].Values, string.Equals(chartType, "donut", StringComparison.Ordinal), token); }
        else { DrawCartesian(canvas, bounds, content, labels, series, chartType, theme, token); }
    }

    private static List<ChartSeries> ParseSeries(JsonElement content, int labelCount)
    {
        var result = new List<ChartSeries>();
        if (!content.TryGetProperty("series", out var array) || array.ValueKind != JsonValueKind.Array) { return result; }
        foreach (var item in array.EnumerateArray())
        {
            var values = ReportJson.Numbers(item, "values");
            if (values.Count != labelCount || values.Any(value => !double.IsFinite(value) || value < 0))
            { throw new ArgumentException("chart.series values must match labels and be finite nonnegative numbers."); }
            result.Add(new(ReportJson.Text(item, "name", $"Series {result.Count + 1}"), values));
        }
        return result;
    }

    private static void DrawCartesian(SKCanvas canvas, SKRect bounds, JsonElement content, IReadOnlyList<string> labels,
        IReadOnlyList<ChartSeries> series, string chartType, FileContextReportTheme theme, CancellationToken token)
    {
        if (series.Count * 150 > bounds.Width - 72)
        { throw new ArgumentException("chart.series legend does not fit the chart width."); }
        var plot = new SKRect(bounds.Left + 72, bounds.Top + 72, bounds.Right - 26, bounds.Bottom - 90);
        var max = string.Equals(chartType, "stacked-bar", StringComparison.Ordinal)
            ? Enumerable.Range(0, labels.Count).Max(index => series.Sum(item => item.Values[index]))
            : series.Max(item => item.Values.Max());
        max = Math.Max(1, max);
        using var grid = ReportCanvas.Paint(new SKColor(220, 227, 233), stroke: 1);
        using var axis = ReportCanvas.Paint(ReportCanvas.Parse(theme.Muted), 11);
        for (var tick = 0; tick <= 4; tick++)
        {
            var y = plot.Bottom - tick * plot.Height / 4;
            canvas.DrawLine(plot.Left, y, plot.Right, y, grid);
            ReportCanvas.Text(canvas, (max * tick / 4).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                bounds.Left + 12, y + 4, axis);
        }
        for (var index = 0; index < labels.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var x = plot.Left + (index + .5f) * plot.Width / labels.Count;
            ReportCanvas.RequireFits(labels[index], axis, plot.Width / labels.Count - 4, $"chart.labels[{index}]");
            ReportCanvas.Text(canvas, labels[index], x - ReportCanvas.MeasureText(labels[index], axis) / 2, plot.Bottom + 18, axis);
            var cumulative = 0d;
            for (var s = 0; s < series.Count; s++)
            {
                var value = series[s].Values[index];
                using var paint = ReportCanvas.Paint(Palette[s % Palette.Length], stroke: string.Equals(chartType, "line", StringComparison.Ordinal) ? 2 : 0);
                var y = plot.Bottom - (float)(value / max * plot.Height);
                if (string.Equals(chartType, "line", StringComparison.Ordinal))
                {
                    if (index > 0)
                    {
                        var priorX = plot.Left + (index - .5f) * plot.Width / labels.Count;
                        var priorY = plot.Bottom - (float)(series[s].Values[index - 1] / max * plot.Height);
                        canvas.DrawLine(priorX, priorY, x, y, paint);
                    }
                    canvas.DrawCircle(x, y, 3, paint);
                }
                else if (string.Equals(chartType, "stacked-bar", StringComparison.Ordinal))
                {
                    var h = (float)(value / max * plot.Height);
                    canvas.DrawRect(x - 14, plot.Bottom - (float)cumulative - h, 28, h, paint);
                    cumulative += h;
                }
                else
                {
                    var groupWidth = Math.Min(50f, plot.Width / labels.Count * .7f);
                    var width = groupWidth / series.Count;
                    canvas.DrawRect(x - groupWidth / 2 + s * width, y, width - 2, plot.Bottom - y, paint);
                }
            }
        }
        DrawAxisLabels(canvas, bounds, plot, content, axis);
        DrawLegend(canvas, bounds, series.Select(item => item.Name).ToArray());
    }

    private static void DrawAxisLabels(SKCanvas canvas, SKRect bounds, SKRect plot,
        JsonElement content, SKPaint axis)
    {
        var xAxis = ReportJson.Text(content, "xAxis");
        var yAxis = ReportJson.Text(content, "yAxis");
        if (xAxis.Length > 0)
        {
            ReportCanvas.RequireFits(xAxis, axis, plot.Width, "chart.xAxis");
            ReportCanvas.Text(canvas, xAxis, plot.Left + (plot.Width - ReportCanvas.MeasureText(xAxis, axis)) / 2, plot.Bottom + 42, axis);
        }
        if (yAxis.Length > 0)
        {
            ReportCanvas.RequireFits(yAxis, axis, bounds.Width - 16, "chart.yAxis");
            ReportCanvas.Text(canvas, yAxis, bounds.Left + 8, plot.Top - 10, axis);
        }
    }

    private static void DrawPie(SKCanvas canvas, SKRect bounds, IReadOnlyList<string> labels,
        IReadOnlyList<double> values, bool donut, CancellationToken token)
    {
        if (labels.Count * 24 > bounds.Height - 100)
        { throw new ArgumentException("chart.labels legend does not fit the chart height."); }
        var total = values.Sum();
        if (total <= 0) { throw new ArgumentException("Pie or donut chart requires a positive total."); }
        var radius = Math.Min(bounds.Height - 140, bounds.Width * .55f) / 2;
        var centerX = bounds.Left + bounds.Width * .42f;
        var centerY = bounds.Top + bounds.Height * .53f;
        var rect = new SKRect(centerX - radius, centerY - radius, centerX + radius, centerY + radius);
        var start = -90f;
        for (var index = 0; index < values.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            using var paint = ReportCanvas.Paint(Palette[index % Palette.Length]);
            var sweep = (float)(values[index] / total * 360);
            canvas.DrawArc(rect, start, sweep, true, paint);
            start += sweep;
        }
        if (donut)
        {
            using var hole = ReportCanvas.Paint(SKColors.White);
            canvas.DrawCircle(centerX, centerY, radius * .53f, hole);
        }
        using var labelPaint = ReportCanvas.Paint(new SKColor(55, 70, 86), 13);
        for (var index = 0; index < labels.Count; index++)
        {
            using var swatch = ReportCanvas.Paint(Palette[index % Palette.Length]);
            canvas.DrawRect(bounds.Right - 245, bounds.Top + 95 + index * 24, 12, 12, swatch);
            ReportCanvas.RequireFits(labels[index], labelPaint, 205, $"chart.labels[{index}]");
            ReportCanvas.Text(canvas, labels[index], bounds.Right - 225, bounds.Top + 106 + index * 24, labelPaint);
        }
    }

    private static void DrawLegend(SKCanvas canvas, SKRect bounds, string[] names)
    {
        using var text = ReportCanvas.Paint(new SKColor(55, 70, 86), 12);
        for (var index = 0; index < names.Length; index++)
        {
            using var swatch = ReportCanvas.Paint(Palette[index % Palette.Length]);
            var x = bounds.Left + 72 + index * 150;
            canvas.DrawRect(x, bounds.Bottom - 30, 11, 11, swatch);
            ReportCanvas.RequireFits(names[index], text, 125, $"chart.series[{index}].name");
            ReportCanvas.Text(canvas, names[index], x + 17, bounds.Bottom - 19, text);
        }
    }

    private sealed record ChartSeries(string Name, IReadOnlyList<double> Values);
}
