namespace ManagedCode.FileContext.Tests;

public sealed class FileContextOperationTimeoutTests
{
    [Theory]
    [InlineData("range")]
    [InlineData("search")]
    public async Task ExpiredDeadline_FailsWholeOperation(string operation)
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var setup = new ManagedCodeStorageFileStore(scope.Storage);
        await setup.WriteAsync("slow.txt", new string('a', 1_000_000));
        var options = new FileContextOptions { OperationTimeout = TimeSpan.FromMilliseconds(1) };
        var store = new ManagedCodeStorageFileStore(scope.Storage, options);
        var context = new FileContextService(store, options);

        var exception = await Should.ThrowAsync<TimeoutException>(() => operation switch
        {
            "range" => (Task)context.ReadRangeAsync("slow.txt", startLine: 2),
            _ => store.SearchAsync("", "^(a+)+b?$"),
        });

        exception.Message.ShouldContain("configured");
    }

    [Theory]
    [InlineData("write")]
    [InlineData("read")]
    [InlineData("delete")]
    [InlineData("list")]
    [InlineData("exists")]
    [InlineData("search")]
    [InlineData("directory")]
    [InlineData("range")]
    [InlineData("info")]
    [InlineData("graph-search")]
    [InlineData("graph-export")]
    public async Task CallerCancellation_IsPreservedForEveryOperation(string operation)
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { OperationTimeout = TimeSpan.FromSeconds(5) };
        var store = new ManagedCodeStorageFileStore(scope.Storage, options);
        var context = new FileContextService(store, options);
        await store.WriteAsync("notes.txt", "unchanged");
        var token = new CancellationToken(canceled: true);

        var exception = await Should.ThrowAsync<OperationCanceledException>(() => InvokeAsync(operation, store, context, token));

        exception.CancellationToken.ShouldBe(token);
        (await store.ReadAsync("notes.txt")).ShouldBe("unchanged");
    }

    [Fact]
    public async Task DisabledDeadline_AllowsLongRangeScan()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { OperationTimeout = null };
        var store = new ManagedCodeStorageFileStore(scope.Storage, options);
        var context = new FileContextService(store, options);
        await store.WriteAsync("slow.txt", new string('a', 100_000));

        var result = await context.ReadRangeAsync("slow.txt", startLine: 2);

        result.TotalLines.ShouldBe(1);
        result.Content.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4_294_967_295)]
    public async Task InvalidOperationTimeout_IsRejectedBeforeStorageAccess(long milliseconds)
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { OperationTimeout = TimeSpan.FromMilliseconds(milliseconds) };

        Should.Throw<InvalidOperationException>(() => new ManagedCodeStorageFileStore(scope.Storage, options));
    }

    [Fact]
    public async Task OversizedRegexTimeout_IsRejectedDuringConfiguration()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { RegexTimeout = TimeSpan.FromMilliseconds(int.MaxValue) };

        Should.Throw<InvalidOperationException>(() => new ManagedCodeStorageFileStore(scope.Storage, options));
    }

    private static Task InvokeAsync(string operation, ManagedCodeStorageFileStore store, FileContextService context, CancellationToken token)
        => operation switch
        {
            "write" => store.WriteAsync("notes.txt", "changed", token),
            "read" => store.ReadAsync("notes.txt", token),
            "delete" => store.DeleteAsync("notes.txt", token),
            "list" => store.ListChildrenAsync("", token),
            "exists" => store.FileExistsAsync("notes.txt", token),
            "search" => store.SearchAsync("", "unchanged", cancellationToken: token),
            "directory" => store.CreateDirectoryAsync("docs", token),
            "range" => context.ReadRangeAsync("notes.txt", cancellationToken: token),
            "info" => context.GetInfoAsync("notes.txt", token),
            "graph-search" => context.SearchMarkdownGraphAsync("context", cancellationToken: token),
            "graph-export" => context.ExportMarkdownGraphAsync(MarkdownGraphFormat.Mermaid, cancellationToken: token),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
}
