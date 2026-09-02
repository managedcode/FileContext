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
dotnet test tests/ManagedCode.FileContext.Tests/ManagedCode.FileContext.Tests.csproj --configuration Release /p:CollectCoverage=true /p:CoverletOutput=coverage /p:CoverletOutputFormat=opencover
dotnet pack src/ManagedCode.FileContext/ManagedCode.FileContext.csproj --configuration Release --no-build --output artifacts
```

The tests use a real `ManagedCode.Storage.FileSystem` root in a unique temporary directory. The agent tool-loop test starts a real LlmTck HTTP host on an ephemeral loopback port; it does not call a live model.

NuGet publication is intentionally absent from local commands. The tag-driven GitHub Actions release workflow is the only publisher.
