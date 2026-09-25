namespace ManagedCode.FileContext;

internal sealed record FileContextReportOutput(string FileName, string ContentType, byte[] Bytes);
