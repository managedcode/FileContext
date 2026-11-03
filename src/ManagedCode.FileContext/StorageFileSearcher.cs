using System.Text;
using System.Text.RegularExpressions;
using ManagedCode.Storage.Core.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.FileSystemGlobbing;

namespace ManagedCode.FileContext;

internal sealed class StorageFileSearcher(
    ManagedCodeStorageFileStore fileStore,
    FileContextOptions options)
{
    public async Task<IReadOnlyList<FileSearchResult>> SearchAsync(
        string directory,
        string regexPattern,
        string? globPattern,
        bool recursive,
        CancellationToken cancellationToken)
    {
        var normalizedDirectory = StoragePathScope.Normalize(directory, allowEmpty: true);
        var regex = new Regex(
            regexPattern,
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
            options.RegexTimeout);
        var matcher = CreateMatcher(globPattern);
        var results = new List<FileSearchResult>();
        var inspectedFiles = 0;

        await foreach (var (path, metadata) in fileStore.EnumerateScopedFilesAsync(normalizedDirectory, cancellationToken).ConfigureAwait(false))
        {
            if (!ShouldSearch(path, normalizedDirectory, recursive, metadata, matcher))
            {
                continue;
            }

            if (++inspectedFiles > options.MaximumSearchFiles || results.Count >= options.MaximumSearchResults)
            {
                break;
            }

            var result = await SearchFileAsync(path, regex, cancellationToken).ConfigureAwait(false);
            if (result is not null)
            {
                results.Add(result);
            }
        }

        return results;
    }

    private static Matcher? CreateMatcher(string? globPattern)
    {
        if (string.IsNullOrWhiteSpace(globPattern))
        {
            return null;
        }

        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(globPattern);
        return matcher;
    }

    private bool ShouldSearch(
        string path,
        string directory,
        bool recursive,
        BlobMetadata metadata,
        Matcher? matcher)
    {
        if (StorageTextPolicy.IsBinaryDocument(path) || metadata.Length > (ulong)options.MaximumSearchFileBytes
            || !StoragePathScope.TryGetRemainder(path, directory, out var relative))
        {
            return false;
        }

        return (recursive || !relative.Contains('/', StringComparison.Ordinal))
            && (matcher is null || matcher.Match(relative).HasMatches);
    }

    private async Task<FileSearchResult?> SearchFileAsync(
        string path,
        Regex regex,
        CancellationToken cancellationToken)
    {
        var stream = await fileStore.OpenReadInOperationAsync(path, cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            var matches = await SearchLinesAsync(stream, regex, options, cancellationToken).ConfigureAwait(false);
            return matches.Count == 0
                ? null
                : new FileSearchResult { FileName = path, Snippet = matches[0].Line, MatchingLines = matches };
        }
    }

    internal static async Task<List<FileSearchMatch>> SearchLinesAsync(
        Stream stream, Regex regex, FileContextOptions options, CancellationToken cancellationToken)
    {
        using var bounded = new StorageReadBudgetStream(stream, options.MaximumSearchFileBytes);
        using var reader = new StreamReader(bounded, Encoding.UTF8, true, leaveOpen: true);
        var lineReader = new StorageTextLineReader(reader, long.MaxValue);
        StringBuilder? splitLine = null;
        var matches = new List<FileSearchMatch>();
        var lineNumber = 0;

        while (matches.Count < options.MaximumMatchesPerFile &&
            await lineReader.HasNextLineAsync(cancellationToken).ConfigureAwait(false))
        {
            lineNumber++;
            if (!lineReader.TryReadBufferedLine(out var line))
            {
                splitLine ??= new StringBuilder();
                splitLine.Clear();
                await lineReader.ReadLineAsync(splitLine, 0, cancellationToken).ConfigureAwait(false);
                line = splitLine.ToString().AsMemory();
            }

            if (IsMatch(regex, line.Span, cancellationToken))
            {
                matches.Add(new FileSearchMatch { LineNumber = lineNumber, Line = line.ToString() });
            }
        }

        return matches;
    }

    private static bool IsMatch(Regex regex, ReadOnlySpan<char> line, CancellationToken cancellationToken)
    {
        try
        {
            return regex.IsMatch(line);
        }
        catch (RegexMatchTimeoutException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }
}
