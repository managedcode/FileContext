using UglyToad.PdfPig.Content;

namespace ManagedCode.FileContext.Pdf;

internal static class FileContextPdfImageBudget
{
    public static void Validate(Page page, FileContextOptions options)
    {
        long pixels = 0;
        var count = 0;
        foreach (var image in page.GetImages())
        {
            count++;
            var imagePixels = (long)image.WidthInSamples * image.HeightInSamples;
            if (image.WidthInSamples <= 0 || image.HeightInSamples <= 0
                || imagePixels > options.MaximumDecodedPdfImagePixels - pixels)
            {
                throw new IOException("The PDF page exceeds the decoded image pixel limit.");
            }
            pixels += imagePixels;
            if (count > options.MaximumImagesPerPdfPage)
            {
                throw new IOException("The PDF page exceeds the embedded image count limit.");
            }
        }
    }
}
