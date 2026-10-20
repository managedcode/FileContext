using System.Net;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Configuration;
using ManagedCode.LlmTestServer.Hosting;
using ManagedCode.LlmTestServer.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ManagedCode.FileContext.Tests.LlmTestServer;

internal sealed class LlmTestServerTestHost(Func<LlmTestServerConfiguration> configurationFactory) : IAsyncDisposable
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
        builder.Services.AddLlmTestServer();

        var application = builder.Build();
        application.MapLlmTestServerToolReplay();
        application.MapLlmTestServer();
        await application.StartAsync(cancellationToken).ConfigureAwait(false);
        _application = application;
        Endpoint = ResolveEndpoint(application);

        using var client = new HttpClient { BaseAddress = Endpoint };
        await new LlmTestServerClient(client).ConfigureAsync(configurationFactory(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<LlmTestServerAssertionSummary> GetAssertionsAsync(CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient { BaseAddress = Endpoint };
        return await new LlmTestServerClient(client).GetAssertionsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_application is not null)
        {
            await _application.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static Uri ResolveEndpoint(WebApplication application)
    {
        var addresses = application.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?.Addresses;
        var address = addresses?.FirstOrDefault(static value => value.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase));
        return address is null
            ? throw new InvalidOperationException("LLM Test Server did not expose a loopback endpoint.")
            : new Uri(address);
    }
}
