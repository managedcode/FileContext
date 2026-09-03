using ManagedCode.Storage.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;

namespace ManagedCode.FileContext.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public async Task DefaultRegistration_ResolvesSharedProviderContracts()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var services = new ServiceCollection();
        services.AddManagedCodeFileContext(scope.Storage, options => options.RequireReadToolApproval = false);
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<AgentFileStore>().ShouldBeOfType<ManagedCodeStorageFileStore>();
        provider.GetRequiredService<IFileContext>().ShouldBeOfType<FileContextService>();
        provider.GetServices<AIContextProvider>().Single().ShouldBeOfType<FileContextProvider>();
    }

    [Fact]
    public async Task KeyedRegistration_UsesMatchingStorageAndRootPrefix()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IStorage>("tenant-a", scope.Storage);
        services.AddKeyedManagedCodeFileContext("tenant-a", options => options.RootPrefix = "tenant-a");
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredKeyedService<AgentFileStore>("tenant-a");
        using var contextProvider = provider.GetRequiredKeyedService<FileContextProvider>("tenant-a");

        await store.WriteAsync("context.txt", "tenant scoped");

        (await store.ReadAsync("context.txt")).ShouldBe("tenant scoped");
        File.Exists(Path.Combine(scope.Directory, "tenant-a", "context.txt")).ShouldBeTrue();
    }
}
