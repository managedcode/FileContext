# API

The primary entry points are:

- `ManagedCodeStorageFileStore` — use when a raw Microsoft `AgentFileStore` is required.
- `FileContextProvider` — add one provider to an Agent Framework agent to receive standard file tools plus range, metadata, and graph tools.
- `IFileContext` — call the extended context operations directly from application code.
- `FileContextDefaults` — reuse the package's named default limits and selectors when configuring or documenting a host.
- `AddManagedCodeFileContext(...)` — register the default storage-backed context services with Microsoft dependency injection.
- `AddKeyedManagedCodeFileContext(...)` — bind the context services to a keyed `IStorage` registration.

See the repository [README](../../README.md) for compiled usage examples. XML documentation is shipped with the NuGet package.
