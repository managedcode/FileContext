using System.Text;
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

    public async Task<FileContextRange> ReadRangeAsync(
        string path,
        int startLine = 1,
        int? lineCount = null,
        CancellationToken cancellationToken = default)
    {
        if (startLine <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startLine), "Line numbers are one-based.");
        }

        var count = lineCount ?? _options.DefaultRangeLineCount;
        if (count <= 0 || count > _options.MaximumRangeLineCount)
        {
            throw new ArgumentOutOfRangeException(nameof(lineCount), $"Line count must be between 1 and {_options.MaximumRangeLineCount}.");
        }

        var metadata = await _fileStore.GetMetadataAsync(path, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"File '{path}' was not found.", path);
        await using var stream = await _fileStore.OpenReadAsync(path, cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: false);
        return await ReadRangeCoreAsync(reader, path, startLine, count, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FileContextInfo?> GetInfoAsync(string path, CancellationToken cancellationToken = default)
    {
        var metadata = await _fileStore.GetMetadataAsync(path, cancellationToken).ConfigureAwait(false);
        return metadata is null
            ? null
            : new FileContextInfo(path, metadata.Length, metadata.MimeType, metadata.LastModified);
    }

    public async Task<MarkdownGraphSearchResult> SearchMarkdownGraphAsync(
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

    public async Task<MarkdownGraphExportResult> ExportMarkdownGraphAsync(
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

            await using var stream = await _fileStore.OpenReadAsync(path, cancellationToken).ConfigureAwait(false);
            var content = await StorageTextReader
                .ReadAsync(stream, _options.MaximumMarkdownSourceBytes, cancellationToken)
                .ConfigureAwait(false);
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

    private async Task<FileContextRange> ReadRangeCoreAsync(
        StreamReader reader,
        string path,
        int startLine,
        int lineCount,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var currentLine = 0;
        var linesAdded = 0;
        var bytesAdded = 0;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            currentLine++;
            if (currentLine < startLine)
            {
                continue;
            }

            if (linesAdded == lineCount)
            {
                return new FileContextRange(path, startLine, currentLine - 1, null, true, builder.ToString());
            }

            bytesAdded = AppendLineWithinLimit(builder, line, bytesAdded);
            linesAdded++;
        }

        var endLine = linesAdded == 0 ? startLine - 1 : startLine + linesAdded - 1;
        return new FileContextRange(path, startLine, endLine, currentLine, false, builder.ToString());
    }

    private int AppendLineWithinLimit(StringBuilder builder, string line, int bytesAdded)
    {
        var separatorBytes = builder.Length == 0 ? 0 : Encoding.UTF8.GetByteCount(Environment.NewLine);
        var nextBytes = bytesAdded + separatorBytes + Encoding.UTF8.GetByteCount(line);
        if (nextBytes > _options.MaximumRangeReadBytes)
        {
            throw new IOException($"Requested range exceeds the configured {_options.MaximumRangeReadBytes}-byte limit.");
        }

        if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        builder.Append(line);
        return nextBytes;
    }
}
