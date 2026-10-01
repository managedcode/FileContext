namespace ManagedCode.FileContext.Pdf;

/// <summary>Controls where synchronous PDF parsers perform random reads.</summary>
public enum FileContextPdfSourceStagingMode
{
    /// <summary>Reuse seekable local files or memory; stage other streams asynchronously to disk.</summary>
    Automatic,

    /// <summary>Stage every input to a temporary file, including seekable local sources.</summary>
    TemporaryFile
}
