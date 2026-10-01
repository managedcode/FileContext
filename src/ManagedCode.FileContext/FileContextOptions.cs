using ManagedCode.FileContext.Pdf;

namespace ManagedCode.FileContext;

/// <summary>Controls file access, approval, search, and graph limits for one context provider.</summary>
public sealed class FileContextOptions
{
    public const string SectionName = "FileContext";

    private static readonly TimeSpan MaximumOperationTimeout = TimeSpan.FromMilliseconds(uint.MaxValue - 1L);
    private static readonly TimeSpan MaximumRegexTimeout = TimeSpan.FromMilliseconds(int.MaxValue - 1);

    /// <summary>Gets or sets a cooperative deadline for each public file/context operation. Null disables the deadline.</summary>
    public TimeSpan? OperationTimeout { get; set; }

    public string RootPrefix { get; set; } = string.Empty;

    public bool EnableWriteTools { get; set; }

    public long MaximumGeneratedFileBytes { get; set; } = FileContextDefaults.MaximumGeneratedFileBytes;

    public bool RequireReadToolApproval { get; set; } = true;

    public bool RequireWriteToolApproval { get; set; } = true;

    public int MaximumPdfReadBytes { get; set; } = FileContextDefaults.MaximumPdfReadBytes;

    /// <summary>Stages cloud streams before synchronous parsing; Automatic reuses local files and memory.</summary>
    public FileContextPdfSourceStagingMode PdfSourceStagingMode { get; set; } = FileContextPdfSourceStagingMode.Automatic;

    /// <summary>Maximum bytes requested by one asynchronous PDF source staging read.</summary>
    public int PdfSourceBufferBytes { get; set; } = FileContextDefaults.PdfSourceBufferBytes;

    /// <summary>Existing directory for temporary PDF sources. Null uses the operating system's temp directory.</summary>
    public string? PdfTemporaryDirectory { get; set; }

    /// <summary>Maximum simultaneous PDF reads/renders per shared processor, including source buffering.</summary>
    public int MaximumConcurrentPdfOperations { get; set; } = FileContextDefaults.MaximumConcurrentPdfOperations;

    public int MaximumImageBytes { get; set; } = FileContextDefaults.MaximumImageBytes;

    /// <summary>Maximum total source-image pixels decoded on one PDF page, independent of output scale.</summary>
    public int MaximumDecodedPdfImagePixels { get; set; } = FileContextDefaults.MaximumDecodedPdfImagePixels;

    public int MaximumRenderedPagePixels { get; set; } = FileContextDefaults.MaximumRenderedPagePixels;

    public int MaximumImagesPerPdfPage { get; set; } = FileContextDefaults.MaximumImagesPerPdfPage;

    public double DefaultPdfPageScale { get; set; } = FileContextDefaults.DefaultPdfPageScale;

    public double MinimumPdfPageScale { get; set; } = FileContextDefaults.MinimumPdfPageScale;

    public double MaximumPdfPageScale { get; set; } = FileContextDefaults.MaximumPdfPageScale;

    public int PdfPngQuality { get; set; } = FileContextDefaults.PdfPngQuality;

    /// <summary>Creates an independent copy for a scoped provider while retaining configured limits.</summary>
    public FileContextOptions Clone() => (FileContextOptions)MemberwiseClone();

    public int MaximumDocxReadBytes { get; set; } = FileContextDefaults.MaximumDocxReadBytes;

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
        ValidatePositive(MaximumGeneratedFileBytes, nameof(MaximumGeneratedFileBytes));
        ValidatePositive(MaximumPdfReadBytes, nameof(MaximumPdfReadBytes));
        ValidatePositive(PdfSourceBufferBytes, nameof(PdfSourceBufferBytes));
        if (PdfSourceStagingMode is not FileContextPdfSourceStagingMode.Automatic
            and not FileContextPdfSourceStagingMode.TemporaryFile)
        {
            throw new InvalidOperationException("The PDF source staging mode is invalid.");
        }
        if (PdfTemporaryDirectory is not null && string.IsNullOrWhiteSpace(PdfTemporaryDirectory))
        {
            throw new InvalidOperationException("The PDF temporary directory must be a nonempty path or null.");
        }
        ValidatePositive(MaximumConcurrentPdfOperations, nameof(MaximumConcurrentPdfOperations));
        ValidatePositive(MaximumImageBytes, nameof(MaximumImageBytes));
        ValidatePositive(MaximumDecodedPdfImagePixels, nameof(MaximumDecodedPdfImagePixels));
        ValidatePositive(MaximumRenderedPagePixels, nameof(MaximumRenderedPagePixels));
        ValidatePositive(MaximumImagesPerPdfPage, nameof(MaximumImagesPerPdfPage));
        if (!double.IsFinite(MinimumPdfPageScale) || MinimumPdfPageScale <= 0
            || !double.IsFinite(DefaultPdfPageScale) || DefaultPdfPageScale < MinimumPdfPageScale
            || !double.IsFinite(MaximumPdfPageScale) || MaximumPdfPageScale < DefaultPdfPageScale)
        {
            throw new InvalidOperationException("PDF page scales must be finite, positive, and ordered.");
        }
        if (PdfPngQuality is < 0 or > FileContextDefaults.MaximumPdfPngQuality)
        {
            throw new InvalidOperationException("PDF PNG quality must be between 0 and 100.");
        }
        ValidatePositive(MaximumDocxReadBytes, nameof(MaximumDocxReadBytes));
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

        if (OperationTimeout is { } timeout && (timeout <= TimeSpan.Zero || timeout > MaximumOperationTimeout))
        {
            throw new InvalidOperationException($"OperationTimeout must be positive and no greater than {MaximumOperationTimeout}, or null to disable it.");
        }

        if (RegexTimeout <= TimeSpan.Zero || RegexTimeout > MaximumRegexTimeout)
        {
            throw new InvalidOperationException("RegexTimeout must be positive and within the supported .NET regex timeout range.");
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
