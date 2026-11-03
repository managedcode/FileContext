// Block validation reports the relevant JSON field in the message.
#pragma warning disable MA0015
using System.Text;
using System.Text.Json;
using SkiaSharp;

namespace ManagedCode.FileContext;

internal static class ReportCanvas
{
    private const float BoldStrokeWidth = .35f;
    private const int SingleMeasurementLength = 256;
    private static readonly Lazy<SKTypeface> Typeface = new(() =>
    {
        using var stream = typeof(ReportCanvas).Assembly.GetManifestResourceStream(
            "ManagedCode.FileContext.Documents.Assets.NotoSans-Regular.ttf")
            ?? throw new InvalidOperationException("The bundled report font is missing.");
        return SKTypeface.FromStream(stream) ?? throw new InvalidOperationException("The bundled report font cannot be loaded.");
    });

    public static ReportPaint Paint(SKColor color, float size = 0, float stroke = 0, bool bold = false) =>
        new(new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = StyleFor(bold, stroke),
            StrokeWidth = bold ? BoldStrokeWidth : stroke
        }, new SKFont { Typeface = Typeface.Value, Size = size });

    private static SKPaintStyle StyleFor(bool bold, float stroke)
    {
        if (bold) { return SKPaintStyle.StrokeAndFill; }
        return stroke > 0 ? SKPaintStyle.Stroke : SKPaintStyle.Fill;
    }

    public static SKColor Parse(string value)
    {
        if (!SKColor.TryParse(value, out var color)) { throw new ArgumentException($"Invalid theme color '{value}'."); }
        return color;
    }

    public static void RequireFits(string value, ReportPaint paint, float width, string field)
    {
        if (MeasureText(value, paint) > width)
        { throw new ArgumentException($"{field} does not fit the available chart space; use a shorter label or a wider page."); }
    }

    public static void Text(SKCanvas canvas, string value, float x, float y, ReportPaint paint) =>
        canvas.DrawText(value, x, y, SKTextAlign.Left, paint.Font, paint.ColorPaint);

    public static float MeasureText(string value, ReportPaint paint) => paint.Font.MeasureText(value, paint.ColorPaint);

    public static IReadOnlyList<string> Wrap(string value, ReportPaint paint, float width)
    {
        var lines = new List<string>();
        foreach (var paragraph in value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            if (paragraph.Length <= SingleMeasurementLength && Ascii.IsValid(paragraph) && MeasureText(paragraph, paint) <= width)
            {
                lines.Add(paragraph);
                continue;
            }

            var start = 0;
            var position = 0;
            foreach (var rune in paragraph.EnumerateRunes())
            {
                var end = position + rune.Utf16SequenceLength;
                if (position > start && paint.Font.MeasureText(paragraph.AsSpan(start, end - start), paint.ColorPaint) > width)
                {
                    lines.Add(paragraph[start..position]);
                    start = position;
                }
                position = end;
            }
            lines.Add(paragraph[start..]);
        }
        return lines;
    }

}
