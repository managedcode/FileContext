# Contributing

Issues and focused pull requests are welcome.

## Development flow

1. Read [AGENTS.md](AGENTS.md) and [the architecture map](docs/Architecture.md).
2. Add or update tests with each behavior change.
3. Run the local quality gates:

   ```bash
   dotnet restore ManagedCode.FileContext.slnx
   dotnet format ManagedCode.FileContext.slnx --verify-no-changes --no-restore
   dotnet build ManagedCode.FileContext.slnx --configuration Release --no-restore
   dotnet test tests/ManagedCode.FileContext.Tests/ManagedCode.FileContext.Tests.csproj --configuration Release --no-build /p:CollectCoverage=true
   ```

4. Explain public API, dependency, configuration, and security changes in the pull request.

The test command enforces at least 95% total line coverage for product code. Keep product code provider-neutral: concrete storage providers and LlmTck belong in tests or host applications. Do not commit credentials, generated test artifacts, or packages. NuGet publication is performed only by the repository release workflow.
