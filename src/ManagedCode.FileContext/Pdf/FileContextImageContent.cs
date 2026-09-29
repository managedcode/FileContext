using Microsoft.Extensions.AI;

namespace ManagedCode.FileContext.Pdf;

/// <summary>Creates model-visible PNG content from bytes, base64, or a remote HTTPS reference.</summary>
public static class FileContextImageContent
{
    public const string PngMediaType = "image/png";

    private const string PngSignatureHex = "89504E470D0A1A0A";
    private static readonly byte[] PngSignature = Convert.FromHexString(PngSignatureHex);

    public static DataContent FromPngBytes(ReadOnlyMemory<byte> bytes, FileContextOptions? options = null)
    {
        ValidatePng(bytes, options);
        return new DataContent(bytes, PngMediaType);
    }

    public static DataContent FromBase64Png(string base64, FileContextOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base64);
        return FromPngBytes(Convert.FromBase64String(base64), options);
    }

    public static string ToBase64Png(ReadOnlyMemory<byte> bytes, FileContextOptions? options = null)
    {
        ValidatePng(bytes, options);
        return Convert.ToBase64String(bytes.Span);
    }

    public static UriContent FromPngUrl(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!url.IsAbsoluteUri || !string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The image URL must be an absolute HTTPS address.", nameof(url));
        }
        return new UriContent(url, PngMediaType);
    }

    private static void ValidatePng(ReadOnlyMemory<byte> bytes, FileContextOptions? options)
    {
        var settings = options ?? new FileContextOptions();
        settings.Validate();
        if (bytes.Length > settings.MaximumImageBytes)
        {
            throw new IOException("The PNG image exceeds the configured byte limit.");
        }
        if (!bytes.Span.StartsWith(PngSignature))
        {
            throw new InvalidDataException("The image is not a PNG file.");
        }
    }
}
