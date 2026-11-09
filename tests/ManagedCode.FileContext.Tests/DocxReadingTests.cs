using ManagedCode.Storage.Core.Models;

namespace ManagedCode.FileContext.Tests;

public sealed class DocxReadingTests
{
    private const string PathInScope = "memories/scan.DOCX";
    private const int CharacterBudget = 20_000;

    [Fact]
    public async Task Docx_preserves_inline_separators_across_a_character_cursor()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { RootPrefix = "chat", MaximumDocxTextCharacters = 6 };
        await UploadAsync(scope, FileContextDocxTestFiles.WithInlineSeparators());
        var context = new FileContextService(new ManagedCodeStorageFileStore(scope.Storage, options), options);

        var first = await context.Documents.ReadDocxTextAsync(PathInScope);
        first.Paragraphs.Single().Text.ShouldBe("A\tB\nC\n");
        first.NextParagraph.ShouldBe(1);
        first.NextCharacter.ShouldBe(6);

        var second = await context.Documents.ReadDocxTextAsync(PathInScope, first.NextParagraph!.Value, first.NextCharacter);
        second.Paragraphs.Single().Text.ShouldBe("D");
        second.NextParagraph.ShouldBeNull();
    }

    [Fact]
    public async Task Resumed_window_keeps_text_box_paragraphs_inside_their_outer_paragraph()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { RootPrefix = "chat" };
        await UploadAsync(scope, FileContextDocxTestFiles.WithTextBox());
        var context = new FileContextService(new ManagedCodeStorageFileStore(scope.Storage, options), options);

        var first = await context.Documents.ReadDocxTextAsync(PathInScope, paragraphCount: 1);
        first.Paragraphs.Single().Text.ShouldContain("boxed");
        first.NextParagraph.ShouldBe(2);
        var second = await context.Documents.ReadDocxTextAsync(PathInScope, first.NextParagraph!.Value, first.NextCharacter);

        second.Paragraphs.Select(static paragraph => (paragraph.Number, paragraph.Text)).ShouldBe([(2, "Second"), (3, "Third")]);
        second.NextParagraph.ShouldBeNull();
    }

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
        var options = new FileContextOptions { RootPrefix = "chat", MaximumDocxTextCharacters = CharacterBudget };
        await UploadAsync(scope, FileContextDocxTestFiles.WithParagraphs(new string('X', 21_000), "Last"));
        var context = new FileContextService(new ManagedCodeStorageFileStore(scope.Storage, options), options);

        var first = await context.Documents.ReadDocxTextAsync(PathInScope);
        first.Paragraphs.Single().Text.Length.ShouldBe(CharacterBudget);
        first.NextParagraph.ShouldBe(1);
        first.NextCharacter.ShouldBe(CharacterBudget);

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
        await Should.ThrowAsync<InvalidOperationException>(() => store.ReadAsync($" {PathInScope} "));
        await Should.ThrowAsync<InvalidOperationException>(() => store.ReadAsync($"{PathInScope}/"));
        await Should.ThrowAsync<InvalidOperationException>(() => context.ReadRangeAsync(PathInScope));
        (await store.SearchAsync("", ".", recursive: true)).ShouldBeEmpty();

        options.MaximumDocxReadBytes = 1;
        await Should.ThrowAsync<IOException>(() => context.Documents.ReadDocxTextAsync(PathInScope));
        var other = new FileContextService(new ManagedCodeStorageFileStore(scope.Storage,
            new FileContextOptions { RootPrefix = "other" }));
        await Should.ThrowAsync<FileNotFoundException>(() => other.Documents.ReadDocxTextAsync(PathInScope));
    }

    [Fact]
    public async Task Docx_cursor_resumes_before_a_split_emoji_and_rejects_a_mid_pair_offset()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { RootPrefix = "chat", MaximumDocxTextCharacters = CharacterBudget };
        var context = new FileContextService(new ManagedCodeStorageFileStore(scope.Storage, options), options);
        var prefix = new string('x', CharacterBudget - 1);
        await UploadAsync(scope, FileContextDocxTestFiles.WithParagraphs(prefix + "😀tail"));

        var first = await context.Documents.ReadDocxTextAsync(PathInScope);
        first.Paragraphs.Single().Text.ShouldBe(prefix);
        first.NextCharacter.ShouldBe(prefix.Length);
        var second = await context.Documents.ReadDocxTextAsync(PathInScope, first.NextParagraph!.Value, first.NextCharacter);
        second.Paragraphs.Single().Text.ShouldBe("😀tail");
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            context.Documents.ReadDocxTextAsync(PathInScope, startCharacter: prefix.Length + 1));
    }

    [Fact]
    public async Task Docx_reader_rejects_an_oversized_decompressed_part()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { RootPrefix = "chat", MaximumOpenXmlPartCharacters = 2_048 };
        var context = new FileContextService(new ManagedCodeStorageFileStore(scope.Storage, options), options);
        var bytes = FileContextDocxTestFiles.WithParagraphs(new string('x', 8_192));
        bytes.Length.ShouldBeLessThan(2_048);
        await UploadAsync(scope, bytes);

        await Should.ThrowAsync<System.Xml.XmlException>(() => context.Documents.ReadDocxTextAsync(PathInScope));
    }

    [Fact]
    public async Task Window_that_cannot_fit_the_next_emoji_resumes_without_losing_it()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { RootPrefix = "chat", MaximumDocxTextCharacters = 2 };
        var context = new FileContextService(new ManagedCodeStorageFileStore(scope.Storage, options), options);
        await UploadAsync(scope, FileContextDocxTestFiles.WithParagraphs("A", "🧪B"));

        var first = await context.Documents.ReadDocxTextAsync(PathInScope);
        first.Paragraphs.ShouldHaveSingleItem().Text.ShouldBe("A");
        (first.NextParagraph, first.NextCharacter).ShouldBe((2, 0));

        var second = await context.Documents.ReadDocxTextAsync(PathInScope, 2);
        second.Paragraphs.ShouldHaveSingleItem().Text.ShouldBe("🧪");
        (second.NextParagraph, second.NextCharacter).ShouldBe((2, 2));

        var third = await context.Documents.ReadDocxTextAsync(PathInScope, 2, 2);
        third.Paragraphs.ShouldHaveSingleItem().Text.ShouldBe("B");
        third.NextParagraph.ShouldBeNull();
    }

    [Fact]
    public async Task Exact_end_offset_preserves_empty_paragraph_and_rejects_offset_past_end()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { RootPrefix = "chat" };
        var context = new FileContextService(new ManagedCodeStorageFileStore(scope.Storage, options), options);
        await UploadAsync(scope, FileContextDocxTestFiles.WithParagraphs("аб", "next"));

        var result = await context.Documents.ReadDocxTextAsync(PathInScope, startCharacter: 2);
        result.Paragraphs.Select(p => p.Text).ShouldBe(["", "next"]);
        var error = await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            context.Documents.ReadDocxTextAsync(PathInScope, startCharacter: 3));
        error.ParamName.ShouldBe("startCharacter");
    }

    private static async Task UploadAsync(TestStorageScope scope, byte[] content)
    {
        var result = await scope.Storage.UploadAsync(content,
            new UploadOptions { FileName = $"chat/{PathInScope}" }).ConfigureAwait(false);
        result.IsSuccess.ShouldBeTrue(result.Problem?.Detail);
    }
}
