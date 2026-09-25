using System.Text.Json;

namespace ManagedCode.FileContext;

/// <summary>Content-only request for a branded, paginated report.</summary>
public sealed record FileContextReport(
    string Title,
    string FileName,
    IReadOnlyList<FileContextReportBlock> Blocks,
    IReadOnlyList<string> Outputs,
    string PageSize = "Letter",
    string Orientation = "portrait",
    FileContextReportTheme? Theme = null);
