using System.Text.Json.Serialization;

namespace ManagedCode.FileContext.Pdf;

/// <summary>
///     What was read from a PDF's text layer. <see cref="PageCount"/> is null when the PDF could not be opened;
///     <see cref="TextCut"/> means <see cref="Text"/> is only a prefix of the whole text layer; <see cref="Failure"/>
///     is the first parser exception, kept for logging and never returned to the caller.
/// </summary>
public sealed record FileContextPdfText(
    int? PageCount,
    string Text,
    bool TextCut,
    IReadOnlyList<int> PagesWithoutText,
    string? Error,
    [property: JsonIgnore] Exception? Failure);
