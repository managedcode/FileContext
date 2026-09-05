namespace ManagedCode.FileContext;

/// <summary>Controls file access, approval, search, and graph limits for one context provider.</summary>
public sealed class FileContextOptions
{
    public string RootPrefix { get; set; } = string.Empty;

    public bool EnableWriteTools { get; set; }

    public bool RequireReadToolApproval { get; set; } = true;

    public bool RequireWriteToolApproval { get; set; } = true;

    public long MaximumFullReadBytes { get; set; } = FileContextDefaults.MaximumFullReadBytes;

    public long MaximumRangeReadBytes { get; set; } = FileContextDefaults.MaximumRangeReadBytes;

    public int DefaultRangeLineCount { get; set; } = FileContextDefaults.DefaultRangeLineCount;

    public int MaximumRangeLineCount { get; set; } = FileContextDefaults.MaximumRangeLineCount;

    public int MaximumSearchFiles { get; set; } = FileContextDefaults.MaximumSearchFiles;

    public long MaximumSearchFileBytes { get; set; } = FileContextDefaults.MaximumSearchFileBytes;

    public int MaximumSearchResults { get; set; } = FileContextDefaults.MaximumSearchResults;

    public int MaximumMatchesPerFile { get; set; } = FileContextDefaults.MaximumMatchesPerFile;

    /// <summary>Gets or sets the finite timeout for one regex match against one line, not the entire search.</summary>
    public TimeSpan RegexTimeout { get; set; } = TimeSpan.FromSeconds(FileContextDefaults.RegexTimeoutSeconds);

    public string MarkdownGlob { get; set; } = FileContextDefaults.MarkdownGlob;

    public int MaximumMarkdownFiles { get; set; } = FileContextDefaults.MaximumMarkdownFiles;

    public long MaximumMarkdownSourceBytes { get; set; } = FileContextDefaults.MaximumMarkdownSourceBytes;

    public int MaximumGraphResults { get; set; } = FileContextDefaults.MaximumGraphResults;

    public int MaximumGraphExportCharacters { get; set; } = FileContextDefaults.MaximumGraphExportCharacters;

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
            throw new InvalidOperationException("The default range cannot exceed the maximum range.");
        }

        if (RegexTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("The regex timeout must be greater than zero.");
        }
    }

    private static void ValidatePositive(long value, string parameterName)
    {
        if (value <= 0)
        {
            throw new InvalidOperationException($"The {parameterName} option must be greater than zero.");
        }
    }
}
