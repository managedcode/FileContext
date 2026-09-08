namespace ManagedCode.FileContext.Tests;

public sealed class DocumentValidationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("'quoted")]
    [InlineData("quoted'")]
    [InlineData("12345678901234567890123456789012")]
    public async Task Invalid_sheet_names_are_rejected_without_writing(string name)
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true };
        var context = new FileContextService(new ManagedCodeStorageFileStore(storage.Storage, options), options);
        await Should.ThrowAsync<ArgumentException>(() => context.Documents.CreateWorkbookAsync("x.xlsx", new([new(name, [])])));
        (await context.ListFilesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Invalid_document_shapes_and_format_mismatches_never_persist_outputs()
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true };
        var context = new FileContextService(new ManagedCodeStorageFileStore(storage.Storage, options), options);
        var documents = context.Documents;
        await Should.ThrowAsync<ArgumentException>(() => documents.CreateWorkbookAsync("x.csv", new([])));
        await Should.ThrowAsync<ArgumentException>(() => documents.CreateWorkbookAsync("x.xlsx", new([])));
        await Should.ThrowAsync<ArgumentException>(() => documents.CreateWorkbookAsync("x.xlsx", new([new("same", []), new("SAME", [])])));
        await Should.ThrowAsync<ArgumentException>(() => documents.CreateWorkbookAsync("x.xlsx", new(null!)));
        await Should.ThrowAsync<ArgumentNullException>(() => documents.CreateWorkbookAsync("x.xlsx", null!));
        await Should.ThrowAsync<ArgumentNullException>(() => documents.CreateWorkbookAsync("x.xlsx", new([null!])));
        await Should.ThrowAsync<ArgumentException>(() => documents.CreateWorkbookAsync("x.xlsx", new([new("Sheet", null!)])));
        await Should.ThrowAsync<ArgumentException>(() => documents.CreateWorkbookAsync("x.xlsx", new([new("Sheet", [null!])])));
        await Should.ThrowAsync<ArgumentException>(() => documents.CreateCsvAsync("x.csv", new([], "|")));
        await Should.ThrowAsync<ArgumentException>(() => documents.CreateCsvAsync("x.csv", new(null!)));
        await Should.ThrowAsync<ArgumentException>(() => documents.CreateCsvAsync("x.csv", new([null!])));
        await Should.ThrowAsync<ArgumentNullException>(() => documents.CreateCsvAsync("x.csv", null!));
        await Should.ThrowAsync<ArgumentNullException>(() => documents.CreatePdfAsync("x.pdf", null!));
        await Should.ThrowAsync<ArgumentException>(() => documents.CreatePdfAsync("x.pdf", new(null!)));
        await Should.ThrowAsync<ArgumentException>(() => documents.CreatePdfAsync("x.pdf", new([null!])));
        await Should.ThrowAsync<ArgumentNullException>(() => documents.CreateTextAsync("x.txt", null!));
        (await context.ListFilesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Conflicting_or_invalid_cells_are_rejected()
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true };
        var context = new FileContextService(new ManagedCodeStorageFileStore(storage.Storage, options), options);
        FileContextSpreadsheetCell[] invalid = [new(Text: "x", Number: 1), new(Number: double.NaN), new(Formula: " "), new(Text: new string('x', 32_768))];
        foreach (var cell in invalid)
        {
            await Should.ThrowAsync<ArgumentException>(() => context.Documents.CreateWorkbookAsync("x.xlsx", new([new("Sheet", [[cell]])])));
        }
        await Should.ThrowAsync<ArgumentNullException>(() => context.Documents.CreateWorkbookAsync("x.xlsx", new([new("Sheet", [[null!]])])));
        (await context.ListFilesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Listing_obeys_directory_boundaries_and_count_budget()
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { MaximumSearchFiles = 1 };
        var store = new ManagedCodeStorageFileStore(storage.Storage, options);
        await store.WriteAsync("docs/a.txt", "a");
        await store.WriteAsync("docs/b.txt", "b");
        await store.WriteAsync("docs-other/a.txt", "other");
        var context = new FileContextService(store, options);
        var files = await context.ListFilesAsync("docs");
        files.Count.ShouldBe(1);
        files[0].Path.ShouldStartWith("docs/");
    }
}
