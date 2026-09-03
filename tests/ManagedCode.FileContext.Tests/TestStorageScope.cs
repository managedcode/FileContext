using ManagedCode.Storage.FileSystem;
using ManagedCode.Storage.FileSystem.Options;

namespace ManagedCode.FileContext.Tests;

internal sealed class TestStorageScope : IAsyncDisposable
{
    private TestStorageScope(string directory, FileSystemStorage storage)
    {
        Directory = directory;
        Storage = storage;
    }

    public string Directory { get; }

    public FileSystemStorage Storage { get; }

    public static async Task<TestStorageScope> CreateAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "managedcode-filecontext-tests", Guid.NewGuid().ToString("N"));
        var storage = new FileSystemStorage(new FileSystemStorageOptions
        {
            BaseFolder = directory,
            CreateContainerIfNotExists = true,
        });
        var result = await storage.CreateContainerAsync().ConfigureAwait(false);
        result.IsSuccess.ShouldBeTrue(result.Problem?.Detail);
        return new TestStorageScope(directory, storage);
    }

    public async ValueTask DisposeAsync()
    {
        Storage.Dispose();
        if (System.IO.Directory.Exists(Directory))
        {
            await Task.Run(() => System.IO.Directory.Delete(Directory, recursive: true)).ConfigureAwait(false);
        }
    }
}
