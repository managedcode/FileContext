namespace ManagedCode.FileContext;

internal sealed class StoragePathScope
{
    private const char Separator = '/';
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

        var prefix = $"{_rootPrefix}/";
        return normalized.StartsWith(prefix, StringComparison.Ordinal)
            ? normalized[prefix.Length..]
            : null;
    }

    public static string Normalize(string path, bool allowEmpty = false)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Contains('\\', StringComparison.Ordinal) || path.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("Paths must use forward slashes and cannot contain NUL characters.", nameof(path));
        }

        var trimmed = path.Trim().Trim(Separator);
        if (trimmed.Length == 0)
        {
            return allowEmpty ? string.Empty : throw new ArgumentException("A relative file path is required.", nameof(path));
        }

        if (Path.IsPathRooted(path) || trimmed.Split(Separator).Any(static segment => segment is "." or ".." || segment.Length == 0))
        {
            throw new ArgumentException("The path must be relative and cannot traverse outside the configured root.", nameof(path));
        }

        return trimmed;
    }

    public static bool TryGetRemainder(string path, string directory, out string remainder)
    {
        if (directory.Length == 0)
        {
            remainder = path;
            return remainder.Length > 0;
        }

        var prefix = $"{directory}/";
        if (path.StartsWith(prefix, StringComparison.Ordinal))
        {
            remainder = path[prefix.Length..];
            return remainder.Length > 0;
        }

        remainder = string.Empty;
        return false;
    }

    private string JoinRootAndPath(string normalizedPath)
    {
        return string.IsNullOrEmpty(normalizedPath)
            ? _rootPrefix
            : $"{_rootPrefix}/{normalizedPath}";
    }
}
