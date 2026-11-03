// Block validation reports the relevant JSON field in the message.
#pragma warning disable MA0015
using System.Text;
using System.Text.Json;

namespace ManagedCode.FileContext;

internal static class FileContextReportRenderer
{
    public static IReadOnlyList<FileContextReportOutput> Render(FileContextReport report, FileContextOptions options,
        long maximumOutputBytes, CancellationToken token)
    {
        Validate(report, options.MaximumReportInputBytes, token);
        var theme = report.Theme ?? new FileContextReportTheme();
        var name = StoragePathScope.Normalize(report.FileName);
        var extension = Path.GetExtension(name);
        var stem = string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".html", StringComparison.OrdinalIgnoreCase)
            ? name[..^extension.Length] : name;
        var outputs = new List<FileContextReportOutput>();
        long retainedBytes = 0;
        foreach (var output in report.Outputs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            switch (output.ToLowerInvariant())
            {
                case "pdf":
                    AddOutput(stem + ".pdf", "application/pdf",
                        FileContextReportPdf.Render(report, theme, options.MaximumReportPages, token));
                    break;
                case "html":
                    AddOutput(stem + ".html", "text/html; charset=utf-8",
                        Encoding.UTF8.GetBytes(FileContextReportHtml.Render(report, theme, token)));
                    break;
                case "png":
                    for (var index = 0; index < report.Blocks.Count; index++)
                    {
                        if (!string.Equals(report.Blocks[index].Type, "chart", StringComparison.Ordinal)) { continue; }
                        AddOutput($"{stem}-chart-{index + 1}.png", "image/png", RenderChart(report, index, theme, token));
                    }
                    break;
            }
        }
        return outputs;

        void AddOutput(string fileName, string contentType, byte[] bytes)
        {
            retainedBytes += bytes.LongLength;
            StorageFileCreator.EnsureWithinLimit(retainedBytes, maximumOutputBytes);
            outputs.Add(new(fileName, contentType, bytes));
        }
    }

    private static byte[] RenderChart(FileContextReport report, int index, FileContextReportTheme theme, CancellationToken token)
    {
        try { return FileContextReportChart.RenderPng(report.Blocks[index].Content, theme, token); }
        catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException)
        { throw new ArgumentException($"blocks[{index}] (chart) failed: {error.Message}", nameof(report), error); }
    }

    private static void Validate(FileContextReport report, int maximumInputBytes, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (string.IsNullOrWhiteSpace(report.Title)) { throw new ArgumentException("title is required.", nameof(report)); }
        if (string.IsNullOrWhiteSpace(report.FileName)) { throw new ArgumentException("fileName is required.", nameof(report)); }
        if (report.Blocks is null) { throw new ArgumentException("blocks is required.", nameof(report)); }
        if (report.Outputs is null || report.Outputs.Count == 0 || report.Outputs.Any(value => value is not ("pdf" or "html" or "png")))
        { throw new ArgumentException("outputs must contain pdf, html, or png.", nameof(report)); }
        if (report.PageSize is not ("Letter" or "A4") || report.Orientation is not ("portrait" or "landscape"))
        { throw new ArgumentException("pageSize must be Letter or A4 and orientation must be portrait or landscape.", nameof(report)); }
        if (maximumInputBytes != int.MaxValue)
        {
            ValidateInputSize(report, maximumInputBytes, token);
        }
        for (var index = 0; index < report.Blocks.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var block = report.Blocks[index] ?? throw new ArgumentException($"blocks[{index}] is required.", nameof(report));
            if (block.Type is not ("heading" or "text" or "kpi-row" or "chart" or "table" or "image" or "callout" or "page-break"))
            { throw new ArgumentException($"blocks[{index}].type '{block.Type}' is unsupported.", nameof(report)); }
            if (block.Content.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined))
            { throw new ArgumentException($"blocks[{index}].content must be an object.", nameof(report)); }
        }
    }

    private static void ValidateInputSize(FileContextReport report, int maximumInputBytes, CancellationToken token)
    {
        using var counter = new ReportInputBudgetStream(maximumInputBytes, token);
        try
        {
            JsonSerializer.Serialize(counter, report);
        }
        catch (InvalidDataException exception)
        {
            throw new ArgumentException(exception.Message, nameof(report), exception);
        }
    }
}
