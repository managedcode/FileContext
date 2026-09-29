namespace ManagedCode.FileContext;

/// <summary>Default limits and selectors used by <see cref="FileContextOptions" />.</summary>
public static class FileContextDefaults
{
    public const int MaximumPdfPngQuality = 100;
    public const long MaximumGeneratedFileBytes = 64L * 1024L * 1024L;
    public const int FirstLineNumber = 1;
    public const int MaximumPdfReadBytes = 100 * 1024 * 1024;
    public const int MaximumImageBytes = 8 * 1024 * 1024;
    public const int MaximumRenderedPagePixels = 4_000_000;
    public const int MaximumImagesPerPdfPage = 20;
    public const double DefaultPdfPageScale = 1.5;
    public const double MinimumPdfPageScale = 0.5;
    public const double MaximumPdfPageScale = 3;
    public const int PdfPngQuality = 100;
    public const int MaximumPdfTextCharacters = 50_000;
    public const int MaximumDocxReadBytes = 25 * 1024 * 1024;
    public const int MaximumDocxTextCharacters = 20_000;
    public const int MaximumDocxParagraphsPerRead = 50;
    public const long MaximumFullReadBytes = 1_024 * 1_024;
    public const long MaximumRangeReadBytes = 256 * 1_024;
    public const int DefaultRangeLineCount = 200;
    public const int MaximumRangeLineCount = 1_000;
    public const int MaximumSearchFiles = 500;
    public const long MaximumSearchFileBytes = 4 * 1_024 * 1_024;
    public const int MaximumSearchResults = 100;
    public const int MaximumMatchesPerFile = 20;
    public const int RegexTimeoutSeconds = 2;
    public const string MarkdownGlob = "**/*.md";
    public const int MaximumMarkdownFiles = 100;
    public const long MaximumMarkdownSourceBytes = 1_024 * 1_024;
    public const int MaximumGraphResults = 20;
    public const int MaximumGraphExportCharacters = 200_000;
}
