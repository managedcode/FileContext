using SkiaSharp;

namespace ManagedCode.FileContext;

internal sealed class ReportPaint(SKPaint colorPaint, SKFont font) : IDisposable
{
    public SKPaint ColorPaint { get; } = colorPaint;
    public SKFont Font { get; } = font;

    public static implicit operator SKPaint(ReportPaint paint) => paint.ColorPaint;

    public void Dispose()
    {
        Font.Dispose();
        ColorPaint.Dispose();
    }
}
