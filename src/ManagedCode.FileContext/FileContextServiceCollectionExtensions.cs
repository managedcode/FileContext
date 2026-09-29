using ManagedCode.Storage.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ManagedCode.FileContext;

/// <summary>Registers storage-backed file-context services with Microsoft dependency injection.</summary>
public static class FileContextServiceCollectionExtensions
{
    public static IServiceCollection AddManagedCodeFileContext(
        this IServiceCollection services,
        Action<FileContextOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var builder = services.AddOptions<FileContextOptions>();
        if (configure is not null)
        {
            builder.Configure(configure);
        }
        AddOptionsValidator(services);

        services.TryAddSingleton(static provider => provider.GetRequiredService<IOptions<FileContextOptions>>().Value);
        services.TryAddSingleton<ManagedCodeStorageFileStore>();
        services.TryAddSingleton<AgentFileStore>(static provider => provider.GetRequiredService<ManagedCodeStorageFileStore>());
        services.TryAddSingleton<IFileContext, FileContextService>();
        services.TryAddSingleton<IFileContextPdf>(static provider => (IFileContextPdf)provider.GetRequiredService<IFileContext>());
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
        var optionsName = $"{FileContextOptions.SectionName}:{Guid.NewGuid():N}";
        var builder = services.AddOptions<FileContextOptions>(optionsName);
        if (configure is not null)
        {
            builder.Configure(configure);
        }
        AddOptionsValidator(services);

        services.AddKeyedSingleton<FileContextOptions>(serviceKey, (provider, _) =>
            provider.GetRequiredService<IOptionsMonitor<FileContextOptions>>().Get(optionsName));
        services.AddKeyedSingleton<ManagedCodeStorageFileStore>(serviceKey, (provider, key) =>
            new ManagedCodeStorageFileStore(provider.GetRequiredKeyedService<IStorage>(key),
                provider.GetRequiredKeyedService<FileContextOptions>(key)));
        services.AddKeyedSingleton<IFileContext>(serviceKey, (provider, key) =>
            new FileContextService(provider.GetRequiredKeyedService<ManagedCodeStorageFileStore>(key),
                provider.GetRequiredKeyedService<FileContextOptions>(key)));
        services.AddKeyedSingleton<IFileContextPdf>(serviceKey, (provider, key) =>
            (IFileContextPdf)provider.GetRequiredKeyedService<IFileContext>(key));
        services.AddKeyedSingleton<FileContextProvider>(serviceKey, (provider, key) =>
            new FileContextProvider(
                provider.GetRequiredKeyedService<ManagedCodeStorageFileStore>(key),
                provider.GetRequiredKeyedService<IFileContext>(key),
                provider.GetRequiredKeyedService<FileContextOptions>(key)));
        services.AddKeyedSingleton<AIContextProvider>(serviceKey, (provider, key) =>
            provider.GetRequiredKeyedService<FileContextProvider>(key));
        services.AddKeyedSingleton<AgentFileStore>(serviceKey, (provider, key) =>
            provider.GetRequiredKeyedService<ManagedCodeStorageFileStore>(key));
        return services;
    }

    /// <summary>Binds the FileContext section for hosts that compose scoped providers themselves.</summary>
    public static IServiceCollection AddFileContextOptions(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<FileContextOptions>()
            .Bind(configuration.GetSection(FileContextOptions.SectionName));
        AddOptionsValidator(services);
        return services;
    }

    private static void AddOptionsValidator(IServiceCollection services) =>
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FileContextOptions>, FileContextOptionsValidator>());
}
