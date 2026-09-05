# Testing

The suite is integration-first:

- storage adapter tests exercise actual files through `ManagedCode.Storage.FileSystem`;
- graph tests run the real `ManagedCode.MarkdownLd.Kb` parser, graph builder, search, and serializers;
- dependency-injection tests resolve the actual default and keyed Agent Framework contracts;
- end-to-end tests run Agent Framework function invocation against a real LlmTck HTTP replay service and verify every advertised read, list, grep, write, delete, replace, range, metadata, graph-search, and graph-export tool;
- protocol tests inspect outgoing HTTP messages for matching call/result IDs after empty results, missing files, tool failures, mutations, and multiple calls with sequential or concurrent invocation enabled, including a subsequent request after session serialization/restoration;
- timeout tests cover configured operation expiry, cancellation of every public operation, disabled deadlines, duration validation, and timeout tool results through restored sessions;
- concurrent storage tests write and range-read eight independent files through one shared adapter/service;
- a sparse 1 GiB filesystem test reads bounded line windows repeatedly, rejects full-file loading, caps allocations, and proves that an oversized line fails before it can be buffered in memory.

Every filesystem test owns a unique temporary root and removes it on disposal. Test execution is serialized so process-wide allocation assertions cannot be distorted by another test. No `IStorage`, Agent Framework, Markdown-LD, or LlmTck mocks are used.

## Coverage gate

The test runner is xUnit over VSTest and coverage is collected by `coverlet.msbuild`. The test project sets a total product line-coverage threshold of 95%; falling below it fails both the local coverage command and CI. Test-assembly code is excluded from the product percentage, while branch and method percentages remain visible in the OpenCover report.

Run the exact command from [development setup](../Development/setup.md). The report is generated at `tests/ManagedCode.FileContext.Tests/TestResults/coverage/coverage.opencover.xml` and is ignored by Git.
