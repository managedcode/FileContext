using System.Text;
using System.Text.RegularExpressions;

namespace ManagedCode.FileContext.Tests;

public sealed partial class SearchStreamingTests
{
    [Fact]
    public async Task Search_rejects_source_bytes_beyond_the_metadata_budget()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(new string('a', 64)));
        var options = new FileContextOptions { MaximumSearchFileBytes = 16 };

        await Should.ThrowAsync<IOException>(() =>
            StorageFileSearcher.SearchLinesAsync(stream, Target(), options, CancellationToken.None));
        stream.Position.ShouldBe(17);
    }

    [Fact]
    public async Task Search_preserves_matching_lines_across_buffers_and_mixed_newlines()
    {
        var longLine = new string('a', 8_200) + "target";
        var content = $"miss\r\n{longLine}\rtarget last";
        using var stream = new MemoryStream([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(content)]);
        var options = new FileContextOptions { MaximumSearchFileBytes = stream.Length };

        var matches = await StorageFileSearcher.SearchLinesAsync(stream, Target(), options, CancellationToken.None);

        matches.Select(static match => (match.LineNumber, match.Line)).ShouldBe([(2, longLine), (3, "target last")]);
    }

    [Fact]
    public async Task Search_reuses_long_line_storage_without_retaining_stale_matches()
    {
        var first = new string('a', 17_000) + "target";
        var nonmatch = new string('é', 8_500);
        var last = new string('z', 9_500) + "target";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"{first}\r\n{nonmatch}\n{last}"));

        var matches = await StorageFileSearcher.SearchLinesAsync(stream, Target(), new(), CancellationToken.None);

        matches.Select(static match => (match.LineNumber, match.Line)).ShouldBe([(1, first), (3, last)]);
    }

    [GeneratedRegex("target", RegexOptions.CultureInvariant, 2_000)]
    private static partial Regex Target();
}
