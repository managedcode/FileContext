namespace ManagedCode.FileContext;

/// <summary>Controls file access, approval, search, and graph limits for one context provider.</summary>
public sealed class FileContextOptions
{
    public string RootPrefix { get; set; } = string.Empty;

    public bool EnableWriteTools { get; set; }

    public bool RequireReadToolApproval { get; set; } = true;

    public bool RequireWriteToolApproval { get; set; } = true;

    public long MaximumFullReadBytes { get; set; } = 1_048_576;

    public long MaximumRangeReadBytes { get; set; } = 262_144;

    public int DefaultRangeLineCount { get; set; } = 200;

    public int MaximumRangeLineCount { get; set; } = 1_000;

    public int MaximumSearchFiles { get; set; } = 500;

    public long MaximumSearchFileBytes { get; set; } = 4_194_304;

    public int MaximumSearchResults { get; set; } = 100;

    public int MaximumMatchesPerFile { get; set; } = 20;

    public TimeSpan RegexTimeout { get; set; } = TimeSpan.FromSeconds(2);

    public string MarkdownGlob { get; set; } = "**/*.md";

    public int MaximumMarkdownFiles { get; set; } = 100;

    public long MaximumMarkdownSourceBytes { get; set; } = 1_048_576;

    public int MaximumGraphResults { get; set; } = 20;

    public int MaximumGraphExportCharacters { get; set; } = 200_000;

    internal void Validate()
    {
        ValidatePositive(MaximumFullReadBytes, nameof(MaximumFullReadBytes));
        ValidatePositive(MaximumRangeReadBytes, nameof(MaximumRangeReadBytes));
        ValidatePositive(DefaultRangeLineCount, nameof(DefaultRangeLineCount));
        ValidatePositive(MaximumRangeLineCount, nameof(MaximumRangeLineCount));
        ValidatePositive(MaximumSearchFiles, nameof(MaximumSearchFiles));
        ValidatePositive(MaximumSearchFileBytes, nameof(MaximumSearchFileBytes));
        ValidatePositive(MaximumSearchResults, nameof(MaximumSearchResults));
        ValidatePositive(MaximumMatchesPerFile, nameof(MaximumMatchesPerFile));
        ValidatePositive(MaximumMarkdownFiles, nameof(MaximumMarkdownFiles));
        ValidatePositive(MaximumMarkdownSourceBytes, nameof(MaximumMarkdownSourceBytes));
        ValidatePositive(MaximumGraphResults, nameof(MaximumGraphResults));
        ValidatePositive(MaximumGraphExportCharacters, nameof(MaximumGraphExportCharacters));

        if (DefaultRangeLineCount > MaximumRangeLineCount)
        {
            throw new ArgumentException("The default range cannot exceed the maximum range.");
        }

        if (RegexTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(RegexTimeout));
        }
    }

    private static void ValidatePositive(long value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
