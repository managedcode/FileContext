namespace ManagedCode.FileContext.Tests;

public sealed class BinaryFileCreationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Upload_preserves_the_caller_stream_and_copies_only_its_remaining_slice(bool publiclyVisible)
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var store = new ManagedCodeStorageFileStore(scope.Storage, new FileContextOptions { EnableWriteTools = true });
        byte[] bytes = [0, 1, 2, 3, 4, 5, 6];
        using var source = new MemoryStream(bytes, 2, 4, writable: false, publiclyVisible) { Position = 1 };

        var created = await store.CreateFileAsync("data.bin", source, "application/octet-stream", maximumBytes: 3);

        created.Length.ShouldBe(3);
        source.Position.ShouldBe(source.Length);
        await using var uploaded = await store.OpenReadAsync(created.Path);
        using var saved = new MemoryStream();
        await uploaded.CopyToAsync(saved);
        saved.ToArray().ShouldBe([3, 4, 5]);
    }
}
