namespace ManagedCode.FileContext.Tests;

public sealed class FileContextServiceTests
{
    [Fact]
    public async Task ReadRange_WhenMoreLinesExist_ReturnsWindowAndContinuationState()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var store = new ManagedCodeStorageFileStore(scope.Storage);
        var service = new FileContextService(store);
        await store.WriteAsync("notes.txt", "one\ntwo\nthree\nfour\nfive");

        var result = await service.ReadRangeAsync("notes.txt", startLine: 2, lineCount: 2);

        result.Content.ShouldBe("two" + Environment.NewLine + "three");
        result.StartLine.ShouldBe(2);
        result.EndLine.ShouldBe(3);
        result.TotalLines.ShouldBeNull();
        result.HasMore.ShouldBeTrue();
    }

    [Fact]
    public async Task ReadRange_WhenEndIsReached_ReturnsTotalLineCount()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var store = new ManagedCodeStorageFileStore(scope.Storage);
        var service = new FileContextService(store);
        await store.WriteAsync("notes.txt", "one\ntwo\nthree");

        var result = await service.ReadRangeAsync("notes.txt", startLine: 2, lineCount: 10);

        result.Content.ShouldBe("two" + Environment.NewLine + "three");
        result.TotalLines.ShouldBe(3);
        result.HasMore.ShouldBeFalse();
    }

    [Fact]
    public async Task MarkdownGraph_WhenMarkdownLinksConcepts_SearchesAndExportsGraph()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var store = new ManagedCodeStorageFileStore(scope.Storage);
        var service = new FileContextService(store);
        await store.WriteAsync("docs/storage.md", """
            ---
            title: Storage Context
            ---
            # Storage Context

            ManagedCode Storage provides files to the [Agent Context](agent.md).
            """);
        await store.WriteAsync("docs/agent.md", "# Agent Context\n\nAgents inspect files using tools.");

        var search = await service.SearchMarkdownGraphAsync("Agent Context", "docs");
        var export = await service.ExportMarkdownGraphAsync(MarkdownGraphFormat.Mermaid, "docs");

        search.DocumentCount.ShouldBe(2);
        search.TripleCount.ShouldBeGreaterThan(0);
        search.Matches.ShouldContain(static match => match.Label.Contains("Agent Context", StringComparison.OrdinalIgnoreCase));
        export.Content.ShouldStartWith("graph LR");
        export.TripleCount.ShouldBeGreaterThan(0);
        export.Truncated.ShouldBeFalse();
    }

    [Fact]
    public async Task GetInfo_WhenFileExists_ReturnsMetadataWithoutContent()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var store = new ManagedCodeStorageFileStore(scope.Storage);
        var service = new FileContextService(store);
        await store.WriteAsync("sample.md", "hello");

        var result = await service.GetInfoAsync("sample.md");

        result.ShouldNotBeNull();
        result.Length.ShouldBe((ulong)5);
        result.Path.ShouldBe("sample.md");
    }

    [Fact]
    public async Task Markdown_graph_reflects_edits_and_deletes_between_operations()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { RootPrefix = "tenant" };
        var store = new ManagedCodeStorageFileStore(scope.Storage, options);
        var context = new FileContextService(store, options);
        var unscoped = new ManagedCodeStorageFileStore(scope.Storage);
        await unscoped.WriteAsync("tenant2/docs/foreign.md", "# Foreign title");
        await store.WriteAsync("docs/edited.md", "# Original title");
        await store.WriteAsync("docs/removed.md", "# Deleted title");

        var before = await context.ExportMarkdownGraphAsync(MarkdownGraphFormat.Turtle, "docs");
        before.DocumentCount.ShouldBe(2);
        before.Content.ShouldContain("Original title");

        await store.WriteAsync("docs/edited.md", "# Revised title");
        (await store.DeleteAsync("docs/removed.md")).ShouldBeTrue();
        var after = await context.ExportMarkdownGraphAsync(MarkdownGraphFormat.Turtle, "docs");

        after.DocumentCount.ShouldBe(1);
        after.Content.ShouldContain("Revised title");
        after.Content.ShouldNotContain("Original title");
        after.Content.ShouldNotContain("Deleted title");
        after.Content.ShouldNotContain("Foreign title");
    }

    [Theory]
    [InlineData(MarkdownGraphFormat.Mermaid, "graph LR")]
    [InlineData(MarkdownGraphFormat.Dot, "digraph")]
    [InlineData(MarkdownGraphFormat.Turtle, "@prefix")]
    [InlineData(MarkdownGraphFormat.JsonLd, "{")]
    public async Task ExportMarkdownGraph_ForEveryFormat_ReturnsFormatSpecificContent(
        MarkdownGraphFormat format,
        string marker)
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var store = new ManagedCodeStorageFileStore(scope.Storage);
        var service = new FileContextService(store);
        await store.WriteAsync("docs/context.md", "# Context\n\nStorage gives agents file context.");

        var result = await service.ExportMarkdownGraphAsync(format, "docs");

        result.Format.ShouldBe(format);
        result.DocumentCount.ShouldBe(1);
        result.TripleCount.ShouldBeGreaterThan(0);
        result.Content.ShouldContain(marker, Case.Insensitive);
    }
}
