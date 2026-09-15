namespace ManagedCode.FileContext;

internal static class StorageTextPolicy
{
    public static bool IsWorkbook(string path) => path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);

    public static void RequireText(string path)
    {
        if (IsWorkbook(path))
        {
            throw new InvalidOperationException($"XLSX is a binary workbook. Use {FileContextToolNames.WorkbookInfo} and {FileContextToolNames.WorkbookRange} to read its sheets and cells.");
        }
    }
}
