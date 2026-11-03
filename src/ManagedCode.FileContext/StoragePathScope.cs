namespace ManagedCode.FileContext;

internal sealed class StoragePathScope
{
    private const char Separator = '/';
    private const string RelativePathRequired = "The path must be relative and cannot traverse outside the configured root.";
    private readonly string _rootPrefix;

    public StoragePathScope(string rootPrefix)
    {
        _rootPrefix = Normalize(rootPrefix, allowEmpty: true);
    }

    public string ToStoragePath(string path, bool allowEmpty = false)
    {
        var normalized = Normalize(path, allowEmpty);
        return string.IsNullOrEmpty(_rootPrefix)
            ? normalized
            : JoinRootAndPath(normalized);
    }

    public string? FromStoragePath(string path)
    {
        var normalized = path.Replace('\\', Separator).Trim(Separator);
        if (string.IsNullOrEmpty(_rootPrefix))
        {
            return normalized;
        }

        if (string.Equals(normalized, _rootPrefix, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return TryGetRemainder(normalized, _rootPrefix, out var remainder) ? remainder : null;
    }

    public static string Normalize(string path, bool allowEmpty = false)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Contains('\\', StringComparison.Ordinal) || path.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("Paths must use forward slashes and cannot contain NUL characters.", nameof(path));
        }

        var trimmed = path.Trim();
        if (Path.IsPathRooted(trimmed) || HasDrivePrefix(trimmed))
        {
            throw new ArgumentException(RelativePathRequired, nameof(path));
        }

        trimmed = trimmed.TrimEnd(Separator);
        if (trimmed.Length == 0)
        {
            return allowEmpty ? string.Empty : throw new ArgumentException("A relative file path is required.", nameof(path));
        }

        var segments = trimmed.AsSpan();
        foreach (var range in segments.Split(Separator))
        {
            if (segments[range] is "." or ".." or [])
            {
                throw new ArgumentException(RelativePathRequired, nameof(path));
            }
        }

        return trimmed;

        static bool HasDrivePrefix(string value) => value.Length >= 2 && char.IsAsciiLetter(value[0]) && value[1] == ':';
    }

    public static bool TryGetRemainder(string path, string directory, out string remainder)
    {
        if (IsDescendant(path, directory))
        {
            remainder = directory.Length == 0 ? path : path[(directory.Length + 1)..];
            return true;
        }

        remainder = string.Empty;
        return false;
    }

    public static bool IsDescendant(string path, string directory) => directory.Length == 0
        ? path.Length > 0
        : path.Length > directory.Length + 1 && path[directory.Length] == Separator &&
            path.StartsWith(directory, StringComparison.Ordinal);

    private string JoinRootAndPath(string normalizedPath)
    {
        return string.IsNullOrEmpty(normalizedPath)
            ? _rootPrefix
            : $"{_rootPrefix}/{normalizedPath}";
    }
}
