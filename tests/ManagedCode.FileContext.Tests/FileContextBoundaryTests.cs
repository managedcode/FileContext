namespace ManagedCode.FileContext.Tests;

public sealed class FileContextBoundaryTests
{
    [Fact]
    public async Task FileQueries_WhenFileIsMissing_ReturnAbsenceOrClearFailure()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var service = new FileContextService(new ManagedCodeStorageFileStore(scope.Storage));

        (await service.GetInfoAsync("missing.txt")).ShouldBeNull();
        await Should.ThrowAsync<FileNotFoundException>(() => service.ReadRangeAsync("missing.txt"));
    }

    [Fact]
    public async Task MarkdownGraph_WhenSiblingDirectorySharesPrefix_ExcludesSiblingDocuments()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var store = new ManagedCodeStorageFileStore(scope.Storage);
        var service = new FileContextService(store);
        await store.WriteAsync("docs/included.md", "# Included document");
        await store.WriteAsync("docs-other/excluded.md", "# Excluded sibling");

        var result = await service.ExportMarkdownGraphAsync(MarkdownGraphFormat.Mermaid, "docs");

        result.DocumentCount.ShouldBe(1);
        result.Content.ShouldNotContain("Excluded sibling");
    }

    [Fact]
    public async Task ReadRange_WhenArgumentsAreOutsideConfiguredBounds_RejectsRequest()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var service = new FileContextService(new ManagedCodeStorageFileStore(scope.Storage));

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => service.ReadRangeAsync("notes.txt", startLine: 0));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => service.ReadRangeAsync("notes.txt", lineCount: 0));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            service.ReadRangeAsync("notes.txt", lineCount: FileContextDefaults.MaximumRangeLineCount + 1));
    }

    [Fact]
    public async Task ReadRange_WhenStartIsPastEnd_ReturnsAnEmptyCompletedWindow()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var store = new ManagedCodeStorageFileStore(scope.Storage);
        var service = new FileContextService(store);
        await store.WriteAsync("notes.txt", "one\ntwo");

        var result = await service.ReadRangeAsync("notes.txt", startLine: 4);

        result.Path.ShouldBe("notes.txt");
        result.Content.ShouldBeEmpty();
        result.StartLine.ShouldBe(4);
        result.EndLine.ShouldBe(3);
        result.TotalLines.ShouldBe(2);
        result.HasMore.ShouldBeFalse();
    }

    [Fact]
    public async Task ReadRange_WhenDecodedWindowExceedsByteLimit_FailsClearly()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { MaximumRangeReadBytes = 4 };
        var store = new ManagedCodeStorageFileStore(scope.Storage, options);
        var service = new FileContextService(store, options);
        await store.WriteAsync("notes.txt", "alpha");

        var exception = await Should.ThrowAsync<IOException>(() => service.ReadRangeAsync("notes.txt"));

        exception.Message.ShouldContain("4-byte limit");
    }

    [Fact]
    public async Task MarkdownGraph_WhenNoDocumentMatches_FailsClearly()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var store = new ManagedCodeStorageFileStore(scope.Storage);
        var service = new FileContextService(store);
        await store.WriteAsync("docs/context.txt", "Not Markdown");

        await Should.ThrowAsync<InvalidOperationException>(() =>
            service.SearchMarkdownGraphAsync("context", "docs"));
    }

    [Fact]
    public async Task MarkdownGraph_WhenLimitsApply_BoundsDocumentsAndExportCharacters()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions
        {
            MaximumMarkdownFiles = 1,
            MaximumGraphExportCharacters = 8,
        };
        var store = new ManagedCodeStorageFileStore(scope.Storage, options);
        var service = new FileContextService(store, options);
        await store.WriteAsync("docs/one.md", "# One\n\nFirst document.");
        await store.WriteAsync("docs/two.md", "# Two\n\nSecond document.");

        var result = await service.ExportMarkdownGraphAsync(MarkdownGraphFormat.Mermaid, "docs");

        result.DocumentCount.ShouldBe(1);
        result.Content.Length.ShouldBe(options.MaximumGraphExportCharacters);
        result.Truncated.ShouldBeTrue();
    }

    [Fact]
    public async Task ExportMarkdownGraph_WhenFormatIsUnknown_RejectsValue()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var store = new ManagedCodeStorageFileStore(scope.Storage);
        var service = new FileContextService(store);
        await store.WriteAsync("context.md", "# Context\n\nStorage-backed context.");

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            service.ExportMarkdownGraphAsync((MarkdownGraphFormat)int.MaxValue));
    }

    [Fact]
    public async Task SearchMarkdownGraph_WhenQueryIsBlank_RejectsRequest()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var service = new FileContextService(new ManagedCodeStorageFileStore(scope.Storage));

        await Should.ThrowAsync<ArgumentException>(() => service.SearchMarkdownGraphAsync(" "));
    }

    [Fact]
    public async Task ReadRange_WhenLineEndingsAndUnicodeVary_PreservesContentAndUtf8Boundaries()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var store = new ManagedCodeStorageFileStore(scope.Storage);
        var service = new FileContextService(store);
        await store.WriteAsync("unicode.txt", "ascii\rβ\r\n€😀\nlast");

        var result = await service.ReadRangeAsync("unicode.txt", lineCount: 4);

        result.Content.ShouldBe(string.Join(Environment.NewLine, "ascii", "β", "€😀", "last"));
        result.EndLine.ShouldBe(4);
        result.TotalLines.ShouldBe(4);
        result.HasMore.ShouldBeFalse();
    }
}
