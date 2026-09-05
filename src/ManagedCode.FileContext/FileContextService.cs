using ManagedCode.MarkdownLd.Kb;
using ManagedCode.MarkdownLd.Kb.Pipeline;
using Microsoft.Extensions.FileSystemGlobbing;

namespace ManagedCode.FileContext;

/// <summary>Implements bounded file operations and Markdown knowledge-graph materialization.</summary>
public sealed class FileContextService : IFileContext
{
    private readonly ManagedCodeStorageFileStore _fileStore;
    private readonly FileContextOptions _options;

    /// <summary>Creates the extended file-context service.</summary>
    public FileContextService(ManagedCodeStorageFileStore fileStore, FileContextOptions? options = null)
    {
        _fileStore = fileStore ?? throw new ArgumentNullException(nameof(fileStore));
        _options = options ?? new FileContextOptions();
        _options.Validate();
    }

    public Task<FileContextRange> ReadRangeAsync(
        string path, int startLine = FileContextDefaults.FirstLineNumber, int? lineCount = null, CancellationToken cancellationToken = default)
        => FileContextOperation.RunAsync(_options.OperationTimeout, token => ReadRangeOperationAsync(path, startLine, lineCount, token), cancellationToken);

    public Task<FileContextInfo?> GetInfoAsync(string path, CancellationToken cancellationToken = default)
        => FileContextOperation.RunAsync(_options.OperationTimeout, token => GetInfoOperationAsync(path, token), cancellationToken);

    public Task<MarkdownGraphSearchResult> SearchMarkdownGraphAsync(string query, string directory = "", CancellationToken cancellationToken = default)
        => FileContextOperation.RunAsync(_options.OperationTimeout, token => SearchMarkdownGraphOperationAsync(query, directory, token), cancellationToken);

    public Task<MarkdownGraphExportResult> ExportMarkdownGraphAsync(MarkdownGraphFormat format, string directory = "", CancellationToken cancellationToken = default)
        => FileContextOperation.RunAsync(_options.OperationTimeout, token => ExportMarkdownGraphOperationAsync(format, directory, token), cancellationToken);

    private async Task<FileContextRange> ReadRangeOperationAsync(
        string path,
        int startLine = FileContextDefaults.FirstLineNumber,
        int? lineCount = null,
        CancellationToken cancellationToken = default)
    {
        if (startLine < FileContextDefaults.FirstLineNumber)
        {
            throw new ArgumentOutOfRangeException(nameof(startLine), "Line numbers are one-based.");
        }

        var count = lineCount ?? _options.DefaultRangeLineCount;
        if (count <= 0 || count > _options.MaximumRangeLineCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lineCount),
                $"Line count must be between {FileContextDefaults.FirstLineNumber} and {_options.MaximumRangeLineCount}.");
        }

        if (await _fileStore.GetMetadataAsync(path, cancellationToken).ConfigureAwait(false) is null)
        {
            throw new FileNotFoundException($"File '{path}' was not found.", path);
        }

        var stream = await _fileStore.OpenReadAsync(path, cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var reader = new StreamReader(stream, leaveOpen: true);
            return await new FileContextRangeReader(_options.MaximumRangeReadBytes)
                .ReadAsync(reader, path, startLine, count, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<FileContextInfo?> GetInfoOperationAsync(string path, CancellationToken cancellationToken = default)
    {
        var metadata = await _fileStore.GetMetadataAsync(path, cancellationToken).ConfigureAwait(false);
        return metadata is null
            ? null
            : new FileContextInfo(path, metadata.Length, metadata.MimeType, metadata.LastModified);
    }

    private async Task<MarkdownGraphSearchResult> SearchMarkdownGraphOperationAsync(
        string query,
        string directory = "",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var (build, documentCount) = await BuildMarkdownGraphAsync(directory, cancellationToken).ConfigureAwait(false);
        var matches = await build.SearchAsync(
            query,
            new KnowledgeGraphRankedSearchOptions { MaxResults = _options.MaximumGraphResults },
            cancellationToken).ConfigureAwait(false);

        return new MarkdownGraphSearchResult(
            documentCount,
            build.Graph.TripleCount,
            matches.Select(static match => new MarkdownGraphMatch(
                match.NodeId,
                match.Label,
                match.Description,
                match.Source.ToString(),
                match.Score)).ToArray());
    }

    private async Task<MarkdownGraphExportResult> ExportMarkdownGraphOperationAsync(
        MarkdownGraphFormat format,
        string directory = "",
        CancellationToken cancellationToken = default)
    {
        var (build, documentCount) = await BuildMarkdownGraphAsync(directory, cancellationToken).ConfigureAwait(false);
        var content = format switch
        {
            MarkdownGraphFormat.Mermaid => build.Graph.SerializeMermaidFlowchart(),
            MarkdownGraphFormat.Dot => build.Graph.SerializeDotGraph(),
            MarkdownGraphFormat.Turtle => build.Graph.SerializeTurtle(),
            MarkdownGraphFormat.JsonLd => build.Graph.SerializeJsonLd(),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
        var truncated = content.Length > _options.MaximumGraphExportCharacters;

        return new MarkdownGraphExportResult(
            format,
            documentCount,
            build.Graph.TripleCount,
            truncated,
            truncated ? content[.._options.MaximumGraphExportCharacters] : content);
    }

    private async Task<(MarkdownKnowledgeBankBuild Build, int DocumentCount)> BuildMarkdownGraphAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        var normalizedDirectory = StoragePathScope.Normalize(directory, allowEmpty: true);
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(_options.MarkdownGlob);
        var sources = new List<MarkdownSourceDocument>();

        await foreach (var metadata in _fileStore.EnumerateScopedFilesAsync(normalizedDirectory, cancellationToken).ConfigureAwait(false))
        {
            var path = _fileStore.ToScopedPath(metadata.FullName);
            if (!matcher.Match(path).HasMatches || metadata.Length > (ulong)_options.MaximumMarkdownSourceBytes)
            {
                continue;
            }

            var stream = await _fileStore.OpenReadAsync(path, cancellationToken).ConfigureAwait(false);
            string content;
            await using (stream.ConfigureAwait(false))
            {
                content = await StorageTextReader
                    .ReadAsync(stream, _options.MaximumMarkdownSourceBytes, cancellationToken)
                    .ConfigureAwait(false);
            }
            sources.Add(new MarkdownSourceDocument(path, content));

            if (sources.Count >= _options.MaximumMarkdownFiles)
            {
                break;
            }
        }

        if (sources.Count == 0)
        {
            throw new InvalidOperationException("No Markdown files matched the configured scope and limits.");
        }

        var bank = new MarkdownKnowledgeBank();
        var build = await bank.BuildAsync(sources, cancellationToken).ConfigureAwait(false);
        return (build, sources.Count);
    }

}
