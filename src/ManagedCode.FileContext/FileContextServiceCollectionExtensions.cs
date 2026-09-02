using ManagedCode.Storage.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ManagedCode.FileContext;

/// <summary>Registers storage-backed file-context services with Microsoft dependency injection.</summary>
public static class FileContextServiceCollectionExtensions
{
    public static IServiceCollection AddManagedCodeFileContext(
        this IServiceCollection services,
        Action<FileContextOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = CreateOptions(configure);

        services.TryAddSingleton(options);
        services.TryAddSingleton<ManagedCodeStorageFileStore>();
        services.TryAddSingleton<AgentFileStore>(static provider => provider.GetRequiredService<ManagedCodeStorageFileStore>());
        services.TryAddSingleton<IFileContext, FileContextService>();
        services.TryAddSingleton<FileContextProvider>();
        services.AddSingleton<AIContextProvider>(
            static provider => provider.GetRequiredService<FileContextProvider>());
        return services;
    }

    public static IServiceCollection AddManagedCodeFileContext(
        this IServiceCollection services,
        IStorage storage,
        Action<FileContextOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(storage);
        services.TryAddSingleton(storage);
        return services.AddManagedCodeFileContext(configure);
    }

    public static IServiceCollection AddKeyedManagedCodeFileContext(
        this IServiceCollection services,
        object serviceKey,
        Action<FileContextOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(serviceKey);
        var options = CreateOptions(configure);

        services.AddKeyedSingleton(serviceKey, options);
        services.AddKeyedSingleton<ManagedCodeStorageFileStore>(serviceKey, (provider, key) =>
            new ManagedCodeStorageFileStore(provider.GetRequiredKeyedService<IStorage>(key), options));
        services.AddKeyedSingleton<IFileContext>(serviceKey, (provider, key) =>
            new FileContextService(provider.GetRequiredKeyedService<ManagedCodeStorageFileStore>(key), options));
        services.AddKeyedSingleton<FileContextProvider>(serviceKey, (provider, key) =>
            new FileContextProvider(
                provider.GetRequiredKeyedService<ManagedCodeStorageFileStore>(key),
                provider.GetRequiredKeyedService<IFileContext>(key),
                options));
        services.AddKeyedSingleton<AIContextProvider>(serviceKey, (provider, key) =>
            provider.GetRequiredKeyedService<FileContextProvider>(key));
        services.AddKeyedSingleton<AgentFileStore>(serviceKey, (provider, key) =>
            provider.GetRequiredKeyedService<ManagedCodeStorageFileStore>(key));
        return services;
    }

    private static FileContextOptions CreateOptions(Action<FileContextOptions>? configure)
    {
        var options = new FileContextOptions();
        configure?.Invoke(options);
        options.Validate();
        return options;
    }
}
