using System.Text;
using System.Text.RegularExpressions;
using ManagedCode.Storage.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ManagedCode.FileContext.Tests;

public sealed class FileContextConfigurationTests
{
    private const string ConfigurationJson = """
        {
          "FileContext": {
            "RootPrefix": "configured",
            "OperationTimeout": "00:00:10",
            "MaximumFullReadBytes": 8,
            "MaximumRangeReadBytes": 8,
            "DefaultRangeLineCount": 1,
            "MaximumRangeLineCount": 2,
            "MaximumSearchFiles": 1,
            "MaximumSearchFileBytes": 64,
            "MaximumSearchResults": 1,
            "MaximumMatchesPerFile": 1,
            "RegexTimeout": "00:00:00.025",
            "MarkdownGlob": "**/*.markdown",
            "MaximumMarkdownFiles": 1,
            "MaximumMarkdownSourceBytes": 128,
            "MaximumGraphResults": 1,
            "MaximumGraphExportCharacters": 32
          }
        }
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfigurationBinding_DefaultAndKeyedRegistrations_ApplyCustomLimits(bool keyed)
    {
        await using var scope = await TestStorageScope.CreateAsync();
        using var json = new MemoryStream(Encoding.UTF8.GetBytes(ConfigurationJson));
        var configuration = new ConfigurationBuilder().AddJsonStream(json).Build();
        using var configurationLifetime = (IDisposable)configuration;
        var services = new ServiceCollection();
        Register(services, scope.Storage, configuration.GetSection("FileContext"), keyed);
        await using var provider = services.BuildServiceProvider();
        var store = Resolve<ManagedCodeStorageFileStore>(provider, keyed);
        var context = Resolve<IFileContext>(provider, keyed);
        var options = Resolve<FileContextOptions>(provider, keyed);

        options.OperationTimeout.ShouldBe(TimeSpan.FromSeconds(10));
        options.RegexTimeout.ShouldBe(TimeSpan.FromMilliseconds(25));
        await AssertFileLimitsAsync(store, context);
        await AssertGraphLimitsAsync(store, context);
        File.Exists(Path.Combine(scope.Directory, "configured", "notes.txt")).ShouldBeTrue();
    }

    [Theory]
    [InlineData(5)]
    [InlineData(25)]
    public async Task Search_UsesConfiguredRegexTimeout(int milliseconds)
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var timeout = TimeSpan.FromMilliseconds(milliseconds);
        var store = new ManagedCodeStorageFileStore(scope.Storage, new FileContextOptions { RegexTimeout = timeout });
        await store.WriteAsync("backtracking.txt", new string('a', 20_000) + "!");

        var exception = await Should.ThrowAsync<RegexMatchTimeoutException>(() => store.SearchAsync("", "^(a+)+$"));

        exception.MatchTimeout.ShouldBe(timeout);
    }

    private static void Register(IServiceCollection services, IStorage storage, IConfiguration section, bool keyed)
    {
        if (keyed)
        {
            services.AddKeyedSingleton("workspace", storage);
            services.AddKeyedManagedCodeFileContext("workspace", options => section.Bind(options));
        }
        else
        {
            services.AddManagedCodeFileContext(storage, options => section.Bind(options));
        }
    }

    private static T Resolve<T>(IServiceProvider provider, bool keyed) where T : notnull
        => keyed ? provider.GetRequiredKeyedService<T>("workspace") : provider.GetRequiredService<T>();

    private static async Task AssertFileLimitsAsync(ManagedCodeStorageFileStore store, IFileContext context)
    {
        await store.WriteAsync("notes.txt", "one\ntwo\nthree").ConfigureAwait(false);
        await store.WriteAsync("long.txt", "0123456789").ConfigureAwait(false);
        await Should.ThrowAsync<IOException>(() => store.ReadAsync("notes.txt")).ConfigureAwait(false);
        (await context.ReadRangeAsync("notes.txt").ConfigureAwait(false)).Content.ShouldBe("one");
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => context.ReadRangeAsync("notes.txt", lineCount: 3)).ConfigureAwait(false);
        await Should.ThrowAsync<IOException>(() => context.ReadRangeAsync("long.txt")).ConfigureAwait(false);
        var matches = await store.SearchAsync("", ".", "notes.txt").ConfigureAwait(false);
        matches.Count.ShouldBe(1);
        matches[0].MatchingLines.Count.ShouldBe(1);
    }

    private static async Task AssertGraphLimitsAsync(ManagedCodeStorageFileStore store, IFileContext context)
    {
        await store.WriteAsync("docs/one.markdown", "# Context One\n\nContext details.").ConfigureAwait(false);
        await store.WriteAsync("docs/two.markdown", "# Context Two\n\nContext details.").ConfigureAwait(false);
        await store.WriteAsync("docs/excluded.md", "# Excluded").ConfigureAwait(false);
        var graph = await context.ExportMarkdownGraphAsync(MarkdownGraphFormat.Mermaid, "docs").ConfigureAwait(false);
        var search = await context.SearchMarkdownGraphAsync("Context", "docs").ConfigureAwait(false);

        graph.DocumentCount.ShouldBe(1);
        graph.Content.Length.ShouldBe(32);
        graph.Truncated.ShouldBeTrue();
        search.Matches.Count.ShouldBe(1);
    }
}
