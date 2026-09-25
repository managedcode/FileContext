// Block validation reports the relevant JSON field in the message.
#pragma warning disable MA0015
using System.Text;
using System.Text.Json;

namespace ManagedCode.FileContext;

internal static class FileContextReportRenderer
{
    private const int MaximumInputBytes = 10_000_000;
    private const int MaximumPages = 60;

    public static IReadOnlyList<FileContextReportOutput> Render(FileContextReport report, CancellationToken cancellationToken)
    {
        Validate(report);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        var theme = report.Theme ?? new FileContextReportTheme();
        var extension = Path.GetExtension(report.FileName);
        var stem = string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".html", StringComparison.OrdinalIgnoreCase)
            ? report.FileName[..^extension.Length] : report.FileName;
        var outputs = new List<FileContextReportOutput>();
        foreach (var output in report.Outputs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            switch (output.ToLowerInvariant())
            {
                case "pdf":
                    outputs.Add(new(stem + ".pdf", "application/pdf",
                        FileContextReportPdf.Render(report, theme, MaximumPages, token)));
                    break;
                case "html":
                    outputs.Add(new(stem + ".html", "text/html; charset=utf-8",
                        Encoding.UTF8.GetBytes(FileContextReportHtml.Render(report, theme, token))));
                    break;
                case "png":
                    for (var index = 0; index < report.Blocks.Count; index++)
                    {
                        if (!string.Equals(report.Blocks[index].Type, "chart", StringComparison.Ordinal)) { continue; }
                        try
                        {
                            outputs.Add(new($"{stem}-chart-{index + 1}.png", "image/png",
                                FileContextReportChart.RenderPng(report.Blocks[index].Content, theme, token)));
                        }
                        catch (Exception error) when (error is not OperationCanceledException)
                        { throw new ArgumentException($"blocks[{index}] (chart) failed: {error.Message}", nameof(report), error); }
                    }
                    break;
            }
        }
        return outputs;
    }

    private static void Validate(FileContextReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (string.IsNullOrWhiteSpace(report.Title)) { throw new ArgumentException("title is required.", nameof(report)); }
        if (string.IsNullOrWhiteSpace(report.FileName)) { throw new ArgumentException("fileName is required.", nameof(report)); }
        if (report.Blocks is null) { throw new ArgumentException("blocks is required.", nameof(report)); }
        if (report.Outputs is null || report.Outputs.Count == 0 || report.Outputs.Any(value => value is not ("pdf" or "html" or "png")))
        { throw new ArgumentException("outputs must contain pdf, html, or png.", nameof(report)); }
        if (report.PageSize is not ("Letter" or "A4") || report.Orientation is not ("portrait" or "landscape"))
        { throw new ArgumentException("pageSize must be Letter or A4 and orientation must be portrait or landscape.", nameof(report)); }
        if (JsonSerializer.SerializeToUtf8Bytes(report).Length > MaximumInputBytes)
        { throw new ArgumentException($"Report input exceeds {MaximumInputBytes} bytes.", nameof(report)); }
        for (var index = 0; index < report.Blocks.Count; index++)
        {
            var block = report.Blocks[index];
            if (block.Type is not ("heading" or "text" or "kpi-row" or "chart" or "table" or "image" or "callout" or "page-break"))
            { throw new ArgumentException($"blocks[{index}].type '{block.Type}' is unsupported.", nameof(report)); }
            if (block.Content.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined))
            { throw new ArgumentException($"blocks[{index}].content must be an object.", nameof(report)); }
        }
        StoragePathScope.Normalize(report.FileName);
    }
}
