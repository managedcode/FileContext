namespace ManagedCode.FileContext;

/// <summary>Provider-neutral metadata for a file.</summary>
public sealed record FileContextInfo(
    string Path,
    ulong Length,
    string? ContentType,
    DateTimeOffset LastModified);
