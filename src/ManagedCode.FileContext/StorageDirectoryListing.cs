using ManagedCode.Storage.Core.Models;
using Microsoft.Agents.AI;

namespace ManagedCode.FileContext;

internal static class StorageDirectoryListing
{
    public static async Task<IReadOnlyList<FileStoreEntry>> ReadAsync(
        IAsyncEnumerable<(string Path, BlobMetadata Metadata)> scopedFiles, string directory, int maximumEntries,
        CancellationToken cancellationToken)
    {
        var children = new Dictionary<string, string>(StringComparer.Ordinal);
        var lookup = children.GetAlternateLookup<ReadOnlySpan<char>>();
        var offset = directory.Length == 0 ? 0 : directory.Length + 1;

        await foreach (var (path, _) in scopedFiles.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var remainder = path.AsSpan(offset);
            var separatorIndex = remainder.IndexOf('/');
            if (separatorIndex >= 0)
            {
                lookup[remainder[..separatorIndex]] = FileStoreEntry.Directory;
            }
            else
            {
                lookup.TryAdd(remainder, FileStoreEntry.File);
            }

            if (children.Count > maximumEntries)
            {
                throw new IOException($"Directory '{directory}' exceeds {nameof(FileContextOptions.MaximumDirectoryEntries)} ({maximumEntries}). Choose a narrower directory.");
            }
        }

        return children
            .OrderBy(static pair => pair.Value, StringComparer.Ordinal)
            .ThenBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => new FileStoreEntry(pair.Key, pair.Value))
            .ToArray();
    }
}
