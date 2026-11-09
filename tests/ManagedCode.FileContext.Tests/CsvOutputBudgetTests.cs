using System.Collections;
using System.Text;

namespace ManagedCode.FileContext.Tests;

public sealed class CsvOutputBudgetTests
{
    [Fact]
    public async Task Stops_at_the_store_budget_before_reading_later_rows()
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var store = new ManagedCodeStorageFileStore(storage.Storage, new() { EnableWriteTools = true, MaximumGeneratedFileBytes = 8 });
        var context = new FileContextService(store, new() { EnableWriteTools = true });

        await Should.ThrowAsync<IOException>(() => context.Documents.CreateCsvAsync("bounded.csv", new(new OversizedRows())));

        (await context.ListFilesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Exact_byte_budget_includes_bom_delimiters_quotes_and_line_endings()
    {
        await using var storage = await TestStorageScope.CreateAsync();
        const string expected = "\uFEFF\"é;😀\";\"\"\"\";\r\n";
        var options = new FileContextOptions { EnableWriteTools = true, MaximumGeneratedFileBytes = Encoding.UTF8.GetByteCount(expected) };
        var store = new ManagedCodeStorageFileStore(storage.Storage, options);
        var context = new FileContextService(store, options);
        var created = await context.Documents.CreateCsvAsync("exact.csv", new([["é;😀", "\"", null]], ";"));

        await using var stream = await store.OpenReadAsync(created.Path);
        using var bytes = new MemoryStream();
        await stream.CopyToAsync(bytes);
        bytes.ToArray().ShouldBe(Encoding.UTF8.GetBytes(expected));
        created.Length.ShouldBe(options.MaximumGeneratedFileBytes);
    }

    [Theory]
    [InlineData(",")]
    [InlineData("\t")]
    public async Task Escaped_multiline_field_fits_exactly_and_one_byte_less_creates_no_file(string delimiter)
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var field = $"a{delimiter}b\r\n🧪\"";
        var expected = $"\uFEFF\"a{delimiter}b\r\n🧪\"\"\"{delimiter}tail\r\n";
        var options = new FileContextOptions { EnableWriteTools = true, MaximumGeneratedFileBytes = Encoding.UTF8.GetByteCount(expected) - 1 };
        var store = new ManagedCodeStorageFileStore(storage.Storage, options);
        var context = new FileContextService(store, options);
        var document = new FileContextCsvDocument([[field, "tail"]], delimiter);

        await Should.ThrowAsync<IOException>(() => context.Documents.CreateCsvAsync("small.csv", document));
        (await context.ListFilesAsync()).ShouldBeEmpty();

        options.MaximumGeneratedFileBytes++;
        var created = await context.Documents.CreateCsvAsync("exact.csv", document);
        await using var source = await store.OpenReadAsync(created.Path);
        using var bytes = new MemoryStream();
        await source.CopyToAsync(bytes);
        bytes.ToArray().ShouldBe(Encoding.UTF8.GetBytes(expected));
    }

    [Fact]
    public async Task Empty_csv_fits_at_exact_utf8_preamble_budget()
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true, MaximumGeneratedFileBytes = Encoding.UTF8.Preamble.Length };
        var store = new ManagedCodeStorageFileStore(storage.Storage, options);
        var context = new FileContextService(store, options);

        var created = await context.Documents.CreateCsvAsync("empty.csv", new([]));

        created.Length.ShouldBe(Encoding.UTF8.Preamble.Length);
        await using var source = await store.OpenReadAsync(created.Path);
        using var bytes = new MemoryStream();
        await source.CopyToAsync(bytes);
        bytes.ToArray().ShouldBe(Encoding.UTF8.GetPreamble());
    }

    private sealed class OversizedRows : IReadOnlyList<IReadOnlyList<string?>>
    {
        public int Count => 2;

        public IReadOnlyList<string?> this[int index] => index == 0
            ? ["an oversized field"]
            : throw new InvalidOperationException("The second row must not be read.");

        public IEnumerator<IReadOnlyList<string?>> GetEnumerator()
        {
            yield return this[0];
            yield return this[1];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
