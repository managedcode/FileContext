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

        await foreach (var metadata in fileStore.EnumerateScopedFilesAsync(normalizedDirectory, cancellationToken).ConfigureAwait(false))
        {
            var path = fileStore.ToScopedPath(metadata.FullName);
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
        if (metadata.Length > (ulong)options.MaximumSearchFileBytes
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
        var stream = await fileStore.OpenReadAsync(path, cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: true);
            var matches = new List<FileSearchMatch>();
            var lineNumber = 0;

            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                lineNumber++;
                cancellationToken.ThrowIfCancellationRequested();
                if (regex.IsMatch(line))
                {
                    matches.Add(new FileSearchMatch { LineNumber = lineNumber, Line = line });
                }

                if (matches.Count >= options.MaximumMatchesPerFile)
                {
                    break;
                }
            }

            return matches.Count == 0
                ? null
                : new FileSearchResult { FileName = path, Snippet = matches[0].Line, MatchingLines = matches };
        }
    }
}
