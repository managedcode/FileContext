namespace ManagedCode.FileContext;

/// <summary>Default limits and selectors used by <see cref="FileContextOptions" />.</summary>
public static class FileContextDefaults
{
    public const int MaximumPdfPngQuality = 100;
    public const long MaximumGeneratedFileBytes = long.MaxValue;
    public const int MaximumReportInputBytes = int.MaxValue;
    public const int MaximumReportPages = int.MaxValue;
    public const int ReportTimeoutSeconds = 0;
    public const int FirstLineNumber = 1;
    public const int MaximumPdfReadBytes = int.MaxValue;
    public const int MaximumConcurrentPdfOperations = int.MaxValue;
    public const int PdfSourceBufferBytes = 81920;
    public const int MaximumImageBytes = int.MaxValue;
    public const int MaximumDecodedPdfImagePixels = int.MaxValue;
    public const int MaximumRenderedPagePixels = int.MaxValue;
    public const int MaximumImagesPerPdfPage = int.MaxValue;
    public const double DefaultPdfPageScale = 1.5;
    public const double MinimumPdfPageScale = float.Epsilon;
    public const double MaximumPdfPageScale = float.MaxValue;
    public const int PdfPngQuality = 100;
    public const int MaximumPdfTextCharacters = int.MaxValue;
    public const int MaximumDocxReadBytes = int.MaxValue;
    public const long MaximumOpenXmlPartCharacters = 0;
    public const int MaximumDocxTextCharacters = int.MaxValue;
    public const int MaximumDocxParagraphsPerRead = int.MaxValue;
    public const long MaximumFullReadBytes = long.MaxValue;
    public const long MaximumRangeReadBytes = long.MaxValue;
    public const int DefaultRangeLineCount = int.MaxValue;
    public const int MaximumRangeLineCount = int.MaxValue;
    public const int MaximumSearchFiles = int.MaxValue;
    public const int MaximumDirectoryEntries = int.MaxValue;
    public const long MaximumSearchFileBytes = long.MaxValue;
    public const int MaximumSearchResults = int.MaxValue;
    public const int MaximumMatchesPerFile = int.MaxValue;
    public const int RegexTimeoutSeconds = -1;
    public const string MarkdownGlob = "**/*.md";
    public const int MaximumMarkdownFiles = int.MaxValue;
    public const long MaximumMarkdownSourceBytes = long.MaxValue;
    public const int MaximumGraphResults = int.MaxValue;
    public const int MaximumGraphExportCharacters = int.MaxValue;
}
