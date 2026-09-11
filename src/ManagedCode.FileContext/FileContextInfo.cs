namespace ManagedCode.FileContext;

/// <summary>Provider-neutral metadata for a file.</summary>
/// <param name="Path">Scoped relative file path.</param>
/// <param name="Length">Exact stored file size in bytes, not characters or model tokens.</param>
/// <param name="ContentType">Provider MIME type, or null when unknown.</param>
/// <param name="LastModified">Provider-reported last modification time.</param>
public sealed record FileContextInfo(
    string Path,
    ulong Length,
    string? ContentType,
    DateTimeOffset LastModified);
