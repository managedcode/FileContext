namespace ManagedCode.FileContext;

/// <summary>Default limits and selectors used by <see cref="FileContextOptions" />.</summary>
public static class FileContextDefaults
{
    public const int FirstLineNumber = 1;
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
