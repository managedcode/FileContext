using System.Text;

namespace ManagedCode.FileContext.Tests;

public sealed class RangeStreamingTests
{
    [Fact]
    public async Task Completed_window_does_not_scan_the_rest_of_the_lookahead_line()
    {
        var bytes = Encoding.UTF8.GetBytes("first\r\n" + new string('x', 8_192));
        using var stream = new SingleReadStream(bytes);
        using var reader = new StreamReader(stream);

        var range = await new FileContextRangeReader(32).ReadAsync(reader, "notes.txt", 1, 1, CancellationToken.None);

        range.Content.ShouldBe("first");
        range.HasMore.ShouldBeTrue();
        range.TotalLines.ShouldBeNull();
    }

    [Fact]
    public async Task Buffer_boundaries_preserve_surrogate_pairs_and_crlf_line_numbers()
    {
        var line = new string('a', 4_095) + "😀";
        var bytes = Encoding.UTF8.GetBytes(line + "\r\nnext\rthird\n");
        using var reader = new StreamReader(new MemoryStream(bytes));

        var range = await new FileContextRangeReader(4_099).ReadAsync(reader, "notes.txt", 1, 1, CancellationToken.None);

        range.Content.ShouldBe(line);
        range.HasMore.ShouldBeTrue();
    }

    private sealed class SingleReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Position == 0
                ? base.ReadAsync(buffer[..64], cancellationToken)
                : throw new InvalidOperationException("The requested window should need only one source read.");
    }
}
