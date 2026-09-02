using Microsoft.Agents.AI;

namespace ManagedCode.FileContext.Tests;

public sealed class ManagedCodeStorageFileStoreTests
{
    [Fact]
    public async Task CrudListAndSearch_WhenUsingRealFileSystemStorage_ProducesObservableResults()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var store = new ManagedCodeStorageFileStore(scope.Storage, new FileContextOptions
        {
            RootPrefix = "workspace",
        });

        await store.WriteAsync("docs/alpha.md", "first line\nneedle value\nlast line");
        await store.WriteAsync("docs/nested/beta.txt", "needle nested");

        (await store.ReadAsync("docs/alpha.md")).ShouldBe("first line\nneedle value\nlast line");
        (await store.FileExistsAsync("docs/alpha.md")).ShouldBeTrue();
        var children = await store.ListChildrenAsync("docs");
        children.Select(static entry => (entry.Name, entry.Type)).ShouldBe([
            ("nested", FileStoreEntry.Directory),
            ("alpha.md", FileStoreEntry.File),
        ]);

        var matches = await store.SearchAsync("docs", "needle", "**/*.md", recursive: true);
        matches.Count.ShouldBe(1);
        matches[0].FileName.ShouldBe("docs/alpha.md");
        matches[0].MatchingLines.Single().LineNumber.ShouldBe(2);

        (await store.DeleteAsync("docs/alpha.md")).ShouldBeTrue();
        (await store.FileExistsAsync("docs/alpha.md")).ShouldBeFalse();
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("docs/../../secret.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("docs\\windows.txt")]
    [InlineData("docs//empty.txt")]
    public async Task Operations_WhenPathEscapesScope_RejectPath(string path)
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var store = new ManagedCodeStorageFileStore(scope.Storage);

        await Should.ThrowAsync<ArgumentException>(() => store.ReadAsync(path));
    }

    [Fact]
    public async Task Read_WhenFileExceedsConfiguredLimit_DirectsCallerToRangeTool()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var store = new ManagedCodeStorageFileStore(scope.Storage, new FileContextOptions
        {
            MaximumFullReadBytes = 8,
        });
        await store.WriteAsync("large.txt", "0123456789");

        var exception = await Should.ThrowAsync<IOException>(() => store.ReadAsync("large.txt"));

        exception.Message.ShouldContain(FileContextToolNames.ReadRange);
    }

    [Fact]
    public async Task Search_WhenRecursiveIsFalse_DoesNotReadNestedFiles()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var store = new ManagedCodeStorageFileStore(scope.Storage);
        await store.WriteAsync("top.txt", "target");
        await store.WriteAsync("nested/deep.txt", "target");

        var matches = await store.SearchAsync("", "target", "*.txt", recursive: false);

        matches.Select(static match => match.FileName).ShouldBe(["top.txt"]);
    }
}
