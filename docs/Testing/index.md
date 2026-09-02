# Testing

The suite is integration-first:

- storage adapter tests exercise actual files through `ManagedCode.Storage.FileSystem`;
- graph tests run the real `ManagedCode.MarkdownLd.Kb` parser, graph builder, search, and serializers;
- dependency-injection tests resolve the actual default and keyed Agent Framework contracts;
- the end-to-end test runs Agent Framework function invocation against a real LlmTck HTTP replay service and verifies that storage-backed content reaches the second model request.

The test runner is xUnit over VSTest. Coverage uses the repository's `coverlet.msbuild` command in [development setup](../Development/setup.md).
