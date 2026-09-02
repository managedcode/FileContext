# ManagedCode.FileContext Package Guide

## Purpose

The package translates `IStorage` operations into Agent Framework `AgentFileStore` behavior and contributes bounded range/metadata/Markdown graph tools through `FileContextProvider`.

## Entry points

- `ManagedCodeStorageFileStore`
- `FileContextProvider`
- `IFileContext`
- `FileContextServiceCollectionExtensions`

## Boundaries

- Use only `IStorage`; concrete providers are forbidden here.
- Normalize every externally supplied path before storage access.
- Standard file tools remain owned by Microsoft Agent Framework's `FileAccessProvider`.
- Markdown types must be mapped immediately into package-owned result records.
- Do not buffer unbounded files or graphs.

## Local command

`dotnet build src/ManagedCode.FileContext/ManagedCode.FileContext.csproj --configuration Release`

## Applicable skills

`mcaf-dotnet`, `mcaf-architecture-overview`, `mcaf-solid-maintainability`, `mcaf-dotnet-quality-ci`.

## Risks

The Agent Framework file-store API currently carries an experimental diagnostic. Keep that suppression explicit and covered by API-level tests.
