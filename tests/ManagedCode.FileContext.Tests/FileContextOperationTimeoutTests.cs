namespace ManagedCode.FileContext.Tests;

public sealed class FileContextOperationTimeoutTests
{
    [Theory]
    [InlineData("range")]
    [InlineData("search")]
    public async Task ExpiredDeadline_FailsWholeOperation(string operation)
    {
        await using var scope = await TestStorageScope.CreateAsync();
        await PrepareSlowSourceAsync(scope, operation);
        var options = new FileContextOptions
        {
            OperationTimeout = TimeSpan.FromMilliseconds(10),
            RegexTimeout = TimeSpan.FromMilliseconds(250),
        };
        var store = new ManagedCodeStorageFileStore(scope.Storage, options);
        var context = new FileContextService(store, options);

        var exception = await Should.ThrowAsync<TimeoutException>(() => operation switch
        {
            "range" => (Task)context.ReadRangeAsync("slow.txt", startLine: 2),
            _ => store.SearchAsync("", "^(a+)+$"),
        });

        exception.Message.ShouldContain("configured");
    }

    private static async Task PrepareSlowSourceAsync(TestStorageScope scope, string operation)
    {
        if (string.Equals(operation, "range", StringComparison.Ordinal))
        {
            // A sparse, unterminated first line keeps the range scan active without writing a large fixture.
            scope.CreateSparseFile("slow.txt", 1L << 30);
            return;
        }

        var store = new ManagedCodeStorageFileStore(scope.Storage);
        await store.WriteAsync("slow.txt", new string('a', 100_000) + "!").ConfigureAwait(false);
    }

    [Theory]
    [InlineData("write")]
    [InlineData("read")]
    [InlineData("open")]
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

    [Fact]
    public async Task A_stream_returned_after_cancellation_is_released_instead_of_leaking()
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new MemoryStream();
        var error = await Should.ThrowAsync<OperationCanceledException>(() =>
            FileContextOperation.RunAsync(TimeSpan.FromSeconds(5), async _ =>
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
                return stream;
            }, cancellation.Token, static result => result.DisposeAsync()));

        error.CancellationToken.ShouldBe(cancellation.Token);
        stream.CanRead.ShouldBeFalse();
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
            "open" => store.OpenReadAsync("notes.txt", token),
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
