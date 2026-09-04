namespace ManagedCode.FileContext.Tests;

public sealed class MultiFileConcurrencyTests
{
    [Fact]
    public async Task IndependentFiles_SupportConcurrentWritesAndRangeReads()
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var store = new ManagedCodeStorageFileStore(storage.Storage);
        var service = new FileContextService(store);
        var paths = Enumerable.Range(0, 8).Select(index => $"file-{index}.txt").ToArray();

        await Task.WhenAll(paths.Select(path => store.WriteAsync(path, $"header\n{path}\nfooter")));
        var results = await Task.WhenAll(paths.Select(path => service.ReadRangeAsync(path, 2, 1)));

        for (var index = 0; index < paths.Length; index++)
        {
            results[index].Path.ShouldBe(paths[index]);
            results[index].Content.ShouldBe(paths[index]);
            results[index].HasMore.ShouldBeTrue();
        }
    }
}
