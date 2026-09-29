namespace ManagedCode.FileContext;

internal static class StorageTextPolicy
{
    public static bool IsBinaryDocument(string path) =>
        path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);

    public static void RequireText(string path)
    {
        if (IsBinaryDocument(path))
        {
            throw new InvalidOperationException($"This Office file is a binary package. Use {FileContextToolNames.DocxText} for DOCX or {FileContextToolNames.WorkbookInfo} and {FileContextToolNames.WorkbookRange} for XLSX.");
        }
    }
}
