# API

The primary entry points are:

- `ManagedCodeStorageFileStore` — use when a raw Microsoft `AgentFileStore` is required.
- `FileContextProvider` — add one provider to an Agent Framework agent to receive standard file tools plus range, metadata, and graph tools.
- `IFileContext` — call the extended context operations directly from application code.
- `FileContextDefaults` — reuse the package's named default limits and selectors when configuring or documenting a host.
- `AddManagedCodeFileContext(...)` — register the default storage-backed context services with Microsoft dependency injection.
- `AddKeyedManagedCodeFileContext(...)` — bind the context services to a keyed `IStorage` registration.

See the repository [README](../../README.md) for compiled usage examples. XML documentation is shipped with the NuGet package.

## Configuration

All `FileContextOptions` size/count limits, `RegexTimeout`, and `OperationTimeout` can be set with the `configure` callback on either `AddManagedCodeFileContext` or `AddKeyedManagedCodeFileContext`. A host may bind an `IConfiguration` section in that callback using `Microsoft.Extensions.Configuration.Binder`; binding happens at registration time.

`RegexTimeout` applies per regex match against one line. `OperationTimeout` is an optional cooperative deadline on every public storage/context API call; null disables it. Configured expiry becomes `TimeoutException`, while caller cancellation remains `OperationCanceledException`. All operations accept a caller-supplied `CancellationToken`, combined with the configured deadline. Internal storage steps use the same token; standard edit tools making several public API calls have one budget per call. Providers and synchronous work must cooperate with cancellation; no background work is abandoned. Provider/model timeouts belong to the host's corresponding client settings. See the [README configuration examples](../../README.md#configure-limits-and-timeouts).
