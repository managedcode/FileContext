# Development setup

## Requirements

- .NET SDK 10.0.400 or a compatible later feature band allowed by `global.json`
- macOS, Linux, or Windows
- no model key or cloud-storage credentials for the default test suite

## Commands

```bash
dotnet restore ManagedCode.FileContext.slnx
dotnet format ManagedCode.FileContext.slnx --verify-no-changes
dotnet build ManagedCode.FileContext.slnx --configuration Release
dotnet test tests/ManagedCode.FileContext.Tests/ManagedCode.FileContext.Tests.csproj --configuration Release
dotnet test tests/ManagedCode.FileContext.Tests/ManagedCode.FileContext.Tests.csproj --configuration Release --no-build /p:CollectCoverage=true
dotnet pack src/ManagedCode.FileContext/ManagedCode.FileContext.csproj --configuration Release --no-build --output artifacts
```

The coverage command inherits its OpenCover output path and 95% total line threshold from the test project. The tests use a real `ManagedCode.Storage.FileSystem` root in a unique temporary directory, including a sparse 1 GiB boundary fixture. Agent tool-loop tests start a real LlmTck HTTP host on an ephemeral loopback port; they do not call a live model.

Static analysis runs as part of every build. The SDK analyzers, Roslynator, Meziantou.Analyzer, and SonarAnalyzer.CSharp are centrally pinned and warnings are errors. Sonar rules `S109` and `S1192` additionally enforce named semantic numbers and repeated strings in product code; ordinary control-flow values such as zero/one and one-off messages remain inline when a constant would obscure intent.

NuGet publication is intentionally absent from local commands. The tag-driven GitHub Actions release workflow is the only publisher.
