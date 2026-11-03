namespace ManagedCode.FileContext.Tests;

public sealed class FileContextOptionsTests
{
    [Fact]
    public async Task Defaults_WhenUsedWithRealFileSystem_AreExposedAsNamedContracts()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions();
        var store = new ManagedCodeStorageFileStore(scope.Storage, options);
        var service = new FileContextService(store, options);
        await store.WriteAsync("defaults.txt", "configured defaults");

        var range = await service.ReadRangeAsync("defaults.txt");

        range.Content.ShouldBe("configured defaults");
        options.MaximumFullReadBytes.ShouldBe(FileContextDefaults.MaximumFullReadBytes);
        options.MaximumRangeReadBytes.ShouldBe(FileContextDefaults.MaximumRangeReadBytes);
        options.DefaultRangeLineCount.ShouldBe(FileContextDefaults.DefaultRangeLineCount);
        options.MaximumRangeLineCount.ShouldBe(FileContextDefaults.MaximumRangeLineCount);
        options.MaximumSearchFiles.ShouldBe(FileContextDefaults.MaximumSearchFiles);
        options.MaximumSearchFileBytes.ShouldBe(FileContextDefaults.MaximumSearchFileBytes);
        options.MaximumSearchResults.ShouldBe(FileContextDefaults.MaximumSearchResults);
        options.MaximumMatchesPerFile.ShouldBe(FileContextDefaults.MaximumMatchesPerFile);
        options.RegexTimeout.ShouldBe(System.Text.RegularExpressions.Regex.InfiniteMatchTimeout);
        options.MarkdownGlob.ShouldBe(FileContextDefaults.MarkdownGlob);
        options.MaximumMarkdownFiles.ShouldBe(FileContextDefaults.MaximumMarkdownFiles);
        options.MaximumMarkdownSourceBytes.ShouldBe(FileContextDefaults.MaximumMarkdownSourceBytes);
        options.MaximumGraphResults.ShouldBe(FileContextDefaults.MaximumGraphResults);
        options.MaximumGraphExportCharacters.ShouldBe(FileContextDefaults.MaximumGraphExportCharacters);
    }

    [Fact]
    public async Task Construction_WhenPositiveLimitIsZero_RejectsConfiguration()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { MaximumSearchResults = 0 };

        var exception = Should.Throw<InvalidOperationException>(() =>
            new ManagedCodeStorageFileStore(scope.Storage, options));

        exception.Message.ShouldContain(nameof(FileContextOptions.MaximumSearchResults));
    }

    [Fact]
    public async Task Construction_WhenDefaultRangeExceedsMaximum_RejectsConfiguration()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions
        {
            MaximumRangeLineCount = 1,
            DefaultRangeLineCount = 2,
        };

        Should.Throw<InvalidOperationException>(() => new ManagedCodeStorageFileStore(scope.Storage, options));
    }

    [Fact]
    public async Task Construction_WhenRegexTimeoutIsNotPositive_RejectsConfiguration()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { RegexTimeout = TimeSpan.Zero };

        Should.Throw<InvalidOperationException>(() => new ManagedCodeStorageFileStore(scope.Storage, options));
    }

    [Fact]
    public async Task Construction_rejects_an_invalid_explicit_docx_or_openxml_limit()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        Should.Throw<InvalidOperationException>(() => new ManagedCodeStorageFileStore(scope.Storage, new() { MaximumDocxTextCharacters = 1 }));
        Should.Throw<InvalidOperationException>(() => new ManagedCodeStorageFileStore(scope.Storage, new() { MaximumOpenXmlPartCharacters = -1 }));
    }
}
