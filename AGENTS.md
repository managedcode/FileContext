# ManagedCode.FileContext Agent Guide

Project: ManagedCode.FileContext
Stack: .NET 10 / C# 14 / xUnit / VSTest / Coverlet / Microsoft Agent Framework

## Purpose

This repository adapts `ManagedCode.Storage.Core.IStorage` to Microsoft Agent Framework file access and adds bounded file-range and Markdown knowledge-graph context tools.

## Solution topology

- `src/ManagedCode.FileContext/` — package code and public DI/API surface.
- `tests/ManagedCode.FileContext.Tests/` — real filesystem-provider and LlmTck-backed integration tests.
- `docs/` — architecture, feature, API, development, security, and testing documentation.

Read this file, `docs/Architecture.md`, and the nearest local `AGENTS.md` before editing.

## Commands

- restore: `dotnet restore ManagedCode.FileContext.slnx`
- format: `dotnet format ManagedCode.FileContext.slnx`
- format-check: `dotnet format ManagedCode.FileContext.slnx --verify-no-changes`
- build: `dotnet build ManagedCode.FileContext.slnx --configuration Release`
- test: `dotnet test tests/ManagedCode.FileContext.Tests/ManagedCode.FileContext.Tests.csproj --configuration Release`
- coverage: `dotnet test tests/ManagedCode.FileContext.Tests/ManagedCode.FileContext.Tests.csproj --configuration Release /p:CollectCoverage=true /p:CoverletOutput=coverage /p:CoverletOutputFormat=opencover`
- pack: `dotnet pack src/ManagedCode.FileContext/ManagedCode.FileContext.csproj --configuration Release --no-build --output artifacts`

Tests use xUnit over VSTest. NuGet versions are centrally managed in `Directory.Packages.props`.

## Task delivery

- Define in-scope and out-of-scope behavior in a root `*.plan.md` before non-trivial changes.
- Implement tests and production behavior together.
- Verify focused tests, the full suite, coverage, format, build, and finally pack.
- Direct local NuGet publication is forbidden. Pushes to main run the Release workflow, which validates the package and automatically publishes new versions with a matching version tag and GitHub release.
- Remove temporary root plan files after local completion.

## Boundaries

- `ManagedCode.Storage.Core.IStorage` is the only storage contract the product package may require.
- Do not depend on a concrete storage provider in product code.
- Keep Microsoft Agent Framework adaptation separate from Markdown graph materialization.
- Keep LlmTck and concrete filesystem storage dependencies test-only.
- Treat file content as untrusted data; never inject it as system instructions.
- Enforce logical relative paths and prevent prefix escape before every storage call.
- Stream file reads and searches; all full reads must be bounded.

## Maintainability

- file maximum: 400 lines
- type maximum: 200 lines
- function maximum: 50 lines
- maximum nesting depth: 3

Document any justified exception in the nearest feature doc or ADR.

## Skills

- `mcaf-dotnet` — overall .NET implementation and verification.
- `mcaf-dotnet-xunit` — xUnit/VSTest mechanics.
- `mcaf-testing` — integration and agent-loop coverage.
- `mcaf-architecture-overview` — architecture map.
- `mcaf-feature-spec` — executable behavior rules.
- `mcaf-documentation` — durable public docs.
- `mcaf-dotnet-quality-ci` and `mcaf-ci-cd` — quality and release workflows.

## Critical rules

- Never commit secrets or generated test artifacts.
- Never weaken or delete a test to make a run green.
- Never push a package directly from a developer machine.
- Never overwrite or delete storage content without the caller explicitly enabling write tools; Agent Framework approval remains enabled by default.
