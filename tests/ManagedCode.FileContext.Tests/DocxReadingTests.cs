using ManagedCode.Storage.Core.Models;

namespace ManagedCode.FileContext.Tests;

public sealed class DocxReadingTests
{
    private const string PathInScope = "memories/scan.DOCX";

    [Fact]
    public async Task Paragraph_windows_include_table_text_and_resume_without_repeating_content()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { RootPrefix = "chat" };
        var store = new ManagedCodeStorageFileStore(scope.Storage, options);
        await UploadAsync(scope, FileContextDocxTestFiles.WithTable());
        var context = new FileContextService(store, options);

        var first = await context.Documents.ReadDocxTextAsync(PathInScope, paragraphCount: 2);
        first.Paragraphs.Select(item => item.Text).ShouldBe(["Before table", "Cell value"]);
        first.NextParagraph.ShouldBe(3);
        first.NextCharacter.ShouldBe(0);

        var second = await context.Documents.ReadDocxTextAsync(PathInScope,
            startParagraph: first.NextParagraph.GetValueOrDefault(), startCharacter: first.NextCharacter);
        second.Paragraphs.Select(item => item.Text).ShouldBe(["After table"]);
        second.NextParagraph.ShouldBeNull();
        (await new FileContextTools(context).DocxTextAsync(PathInScope)).Paragraphs.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Long_paragraph_returns_character_cursor_and_stays_bounded()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { RootPrefix = "chat" };
        await UploadAsync(scope, FileContextDocxTestFiles.WithParagraphs(new string('X', 21_000), "Last"));
        var context = new FileContextService(new ManagedCodeStorageFileStore(scope.Storage, options), options);

        var first = await context.Documents.ReadDocxTextAsync(PathInScope);
        first.Paragraphs.Single().Text.Length.ShouldBe(FileContextDefaults.MaximumDocxTextCharacters);
        first.NextParagraph.ShouldBe(1);
        first.NextCharacter.ShouldBe(FileContextDefaults.MaximumDocxTextCharacters);

        var second = await context.Documents.ReadDocxTextAsync(PathInScope,
            first.NextParagraph.GetValueOrDefault(), first.NextCharacter);
        second.Paragraphs.Select(item => item.Text).ShouldBe([new string('X', 1_000), "Last"]);
        second.NextParagraph.ShouldBeNull();
    }

    [Fact]
    public async Task Docx_reader_enforces_scope_extension_limits_and_binary_text_policy()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { RootPrefix = "chat" };
        var store = new ManagedCodeStorageFileStore(scope.Storage, options);
        await UploadAsync(scope, FileContextDocxTestFiles.WithParagraphs("Safe content"));
        var context = new FileContextService(store, options);

        await Should.ThrowAsync<ArgumentException>(() => context.Documents.ReadDocxTextAsync("../other.docx"));
        await Should.ThrowAsync<ArgumentException>(() => context.Documents.ReadDocxTextAsync("other.pdf"));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => context.Documents.ReadDocxTextAsync(PathInScope, paragraphCount: 0));
        await Should.ThrowAsync<InvalidOperationException>(() => store.ReadAsync(PathInScope));
        await Should.ThrowAsync<InvalidOperationException>(() => context.ReadRangeAsync(PathInScope));
        (await store.SearchAsync("", ".", recursive: true)).ShouldBeEmpty();

        options.MaximumDocxReadBytes = 1;
        await Should.ThrowAsync<IOException>(() => context.Documents.ReadDocxTextAsync(PathInScope));
        var other = new FileContextService(new ManagedCodeStorageFileStore(scope.Storage,
            new FileContextOptions { RootPrefix = "other" }));
        await Should.ThrowAsync<FileNotFoundException>(() => other.Documents.ReadDocxTextAsync(PathInScope));
    }

    private static async Task UploadAsync(TestStorageScope scope, byte[] content)
    {
        var result = await scope.Storage.UploadAsync(content,
            new UploadOptions { FileName = $"chat/{PathInScope}" }).ConfigureAwait(false);
        result.IsSuccess.ShouldBeTrue(result.Problem?.Detail);
    }
}
