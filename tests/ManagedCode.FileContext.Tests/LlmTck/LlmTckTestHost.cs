using System.Net;
using ManagedCode.LlmTck.Client;
using ManagedCode.LlmTck.Configuration;
using ManagedCode.LlmTck.Hosting;
using ManagedCode.LlmTck.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ManagedCode.FileContext.Tests.LlmTck;

internal sealed class LlmTckTestHost(Func<LlmTckConfiguration> configurationFactory) : IAsyncDisposable
{
    private WebApplication? _application;

    public Uri Endpoint { get; private set; } = null!;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
        });
        builder.WebHost.UseKestrel().ConfigureKestrel(static options => options.Listen(IPAddress.Loopback, 0));
        builder.Logging.ClearProviders();
        builder.Services.AddLlmTck();

        var application = builder.Build();
        application.MapLlmTckToolReplay();
        application.MapLlmTck();
        await application.StartAsync(cancellationToken);
        _application = application;
        Endpoint = ResolveEndpoint(application);

        using var client = new HttpClient { BaseAddress = Endpoint };
        await new LlmTckClient(client).ConfigureAsync(configurationFactory(), cancellationToken);
    }

    public async Task<LlmTckAssertionSummary> GetAssertionsAsync(CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient { BaseAddress = Endpoint };
        return await new LlmTckClient(client).GetAssertionsAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_application is not null)
        {
            await _application.DisposeAsync();
        }
    }

    private static Uri ResolveEndpoint(WebApplication application)
    {
        var addresses = application.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?.Addresses;
        var address = addresses?.FirstOrDefault(static value => value.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase));
        return address is null
            ? throw new InvalidOperationException("LLM TCK did not expose a loopback endpoint.")
            : new Uri(address);
    }
}
