# ManagedCode.FileContext Test Guide

## Purpose

Prove storage-adapter, tool, DI, graph, safety, and end-to-end Agent Framework behavior.

## Entry points

- storage adapter tests use `ManagedCode.Storage.FileSystem` against a temporary directory
- graph tests use real Markdown-LD builds
- agent tests use an in-process HTTP LlmTck host and real function invocation

## Boundaries

- Do not mock `IStorage`, Agent Framework, or LlmTck.
- Assert concrete files, content, search matches, graph results, and invoked tool output.
- Keep all temporary data under unique test-owned directories and remove it on disposal.

## Local commands

- focused: `dotnet test tests/ManagedCode.FileContext.Tests/ManagedCode.FileContext.Tests.csproj --configuration Release --filter FullyQualifiedName~TypeName`
- full: `dotnet test tests/ManagedCode.FileContext.Tests/ManagedCode.FileContext.Tests.csproj --configuration Release`

## Applicable skills

`mcaf-testing`, `mcaf-dotnet`, `mcaf-dotnet-xunit`.

## Risks

LlmTck HTTP tests allocate an ephemeral loopback port. They must dispose the host and must not rely on fixed ports or arbitrary sleeps.
