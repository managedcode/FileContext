# Security

## Trust boundaries

- The host selects and configures the `IStorage` provider and credentials.
- Model-supplied paths and regular expressions are untrusted.
- Storage file contents are untrusted reference data and can contain indirect prompt-injection text.
- Write operations can destroy or overwrite persistent data.

## Controls

- Path normalization rejects rooted, traversal, backslash, null-byte, and ambiguous segment inputs.
- `RootPrefix` confines every operation to a configured storage subtree.
- Write tools are disabled by default. If enabled, Agent Framework approval is still required by default.
- File reads, searches, graph sources, graph exports, regex execution, and result counts are bounded.
- The provider advertises tools and safe usage instructions; it does not promote file contents into system instructions.
- Secrets stay inside the configured storage provider. Tool results expose logical paths and content only, never provider connection details.

Applications remain responsible for storage authorization, tenant isolation, content classification, malware scanning, and deciding whether read-tool approval may be disabled.
