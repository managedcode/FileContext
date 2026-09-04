namespace ManagedCode.FileContext;

internal static class FileContextToolDescriptions
{
    public const string RelativeFilePath = "Relative slash-separated file path.";
    public const string OptionalMarkdownDirectory = "Optional relative directory containing Markdown files.";

    public const string ReadRange =
        "Read a bounded, one-based line range from a text file. Use this instead of a full read for large files.";
    public const string StartLine = "One-based first line to read.";
    public const string LineCount = "Number of lines to return; omitted uses the configured default.";
    public const string GetInfo = "Return file size, media type, and last-modified time without reading its content. Fails if the file does not exist.";
    public const string SearchMarkdownGraph =
        "Build a linked-data knowledge graph from scoped Markdown files and search its concepts and relationships.";
    public const string GraphQuery = "Concept or relationship query.";
    public const string ExportMarkdownGraph =
        "Build a linked-data graph from scoped Markdown files and export it as Mermaid, DOT, Turtle, or JSON-LD.";
    public const string GraphFormat = "Export format: Mermaid, Dot, Turtle, or JsonLd.";
}
