namespace ManagedCode.FileContext.Pdf;

/// <summary>An embedded image found on a one-based PDF page.</summary>
public sealed record FileContextPdfImage(int PageNumber, int ImageNumber, byte[] PngBytes);
