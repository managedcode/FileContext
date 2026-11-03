namespace ManagedCode.FileContext;

internal static class FileContextReportImage
{
    private const int MinimumImageBytes = 8;
    private static readonly byte[] PngSignature = [137, 80, 78, 71];
    private static readonly byte[] JpegSignature = [255, 216];

    public static string Validate(byte[] bytes)
    {
        if (bytes.Length < MinimumImageBytes)
        { throw new ArgumentException("image must be a PNG or JPEG.", nameof(bytes)); }
        if (bytes.AsSpan().StartsWith(PngSignature))
        { return "image/png"; }
        if (bytes.AsSpan().StartsWith(JpegSignature))
        { return "image/jpeg"; }
        throw new ArgumentException("image must be PNG or JPEG.", nameof(bytes));
    }
}
