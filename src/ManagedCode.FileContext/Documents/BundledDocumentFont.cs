namespace ManagedCode.FileContext;

internal static class BundledDocumentFont
{
    public static Stream Open() => typeof(BundledDocumentFont).Assembly.GetManifestResourceStream(
        "ManagedCode.FileContext.Documents.Assets.NotoSans-Regular.ttf")
        ?? throw new InvalidOperationException("The bundled document font is missing.");
}
