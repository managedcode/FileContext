# Changelog

All notable changes to ManagedCode.FileContext are documented here.

## 0.0.2 - 2026-09-03

- Enforce a 95% product line-coverage gate in local and CI coverage runs.
- Add centrally managed Roslynator, Meziantou, and Sonar static analysis with magic-number and repeated-string diagnostics.
- Replace semantic default literals with the public `FileContextDefaults` contract and centralize tool descriptions.
- Stream and bound individual range lines so giant unterminated lines cannot cause proportional memory allocation.
- Exercise every Agent Framework file-context tool through real filesystem and LlmTck integration tests, including a sparse 1 GiB allocation boundary.

## 0.0.1 - 2026-09-03

- Adapt ManagedCode.Storage `IStorage` to Microsoft Agent Framework `AgentFileStore`.
- Compose the standard `file_access_*` tools with bounded range, metadata, and Markdown graph tools.
- Add root-prefix isolation, traversal protection, configurable resource limits, and write-safe defaults.
- Add default and keyed Microsoft dependency-injection registration.
- Add real filesystem, Markdown-LD, and LlmTck Agent Framework integration coverage.
