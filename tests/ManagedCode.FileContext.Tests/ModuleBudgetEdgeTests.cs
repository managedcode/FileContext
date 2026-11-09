using System.Text;
using System.Text.Json;

namespace ManagedCode.FileContext.Tests;

public sealed class ModuleBudgetEdgeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Table_metadata_limit_counts_the_same_escaped_utf8_bytes_as_serialization(bool workbook)
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true };
        var store = new ManagedCodeStorageFileStore(scope.Storage, options);
        var context = new FileContextService(store, options);
        const string header = "Кількість 🧪 \"quoted\"";
        var created = workbook
            ? await context.Documents.CreateWorkbookAsync("table.xlsx", new([new("Data", [[new(Text: header)], [new(Number: 4)]])]))
            : await context.Documents.CreateCsvAsync("table.csv", new([[header], ["4"]]));
        var expected = JsonSerializer.SerializeToUtf8Bytes(await context.Documents.GetTablesInfoAsync(created.Path));

        options.MaximumRangeReadBytes = expected.Length;
        var exact = await context.Documents.GetTablesInfoAsync(created.Path);
        JsonSerializer.SerializeToUtf8Bytes(exact).ShouldBe(expected);
        exact.Tables.ShouldHaveSingleItem().Headers.ShouldBe([header]);

        options.MaximumRangeReadBytes--;
        var error = await Should.ThrowAsync<IOException>(() => context.Documents.GetTablesInfoAsync(created.Path));
        error.Message.ShouldContain("output read budget");
    }

    [Fact]
    public async Task Bounded_text_validates_decoded_bytes_and_releases_its_buffer_after_failure()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { MaximumFullReadBytes = 10 };
        var store = new ManagedCodeStorageFileStore(scope.Storage, options);
        const string source = "語言漢字";
        byte[] bytes = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(source)];
        await File.WriteAllBytesAsync(Path.Combine(scope.Directory, "utf16.txt"), bytes);
        await store.WriteAsync("small.txt", "safe");

        var error = await Should.ThrowAsync<IOException>(() => store.ReadAsync("utf16.txt"));
        error.Message.ShouldContain("Decoded file content");
        (await store.ReadAsync("small.txt")).ShouldBe("safe");
        options.MaximumFullReadBytes = 12;
        (await store.ReadAsync("utf16.txt")).ShouldBe(source);
    }
}
