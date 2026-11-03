# Branded reports

`FileContextDocumentService.CreateReportAsync` generates a `FileContextReportResult` containing scoped file references. The caller supplies a title, base file name, ordered blocks, output kinds (`pdf`, `html`, `png`), Letter/A4 page size, portrait/landscape orientation, and server-resolved visual theme. The library never fetches URLs or loads external fonts. A host must resolve and validate remote images before passing base64 PNG/JPEG bytes in an `image` block.

```mermaid
flowchart LR
    A[Host validates input and resolves images] --> B[FileContextReport]
    B --> C[PDF pages]
    B --> D[Self-contained HTML]
    B --> E[PNG chart images]
    C --> F[Scoped IStorage]
    D --> F
    E --> F
```

Block content is JSON:

- `heading`: `title`, optional `subtitle`.
- `text`: `text` with bold spans (`**bold**`), dash bullets, and HTTPS Markdown links.
- `kpi-row`: `items` with entries containing `label`, `value`, optional `change`, and optional `indicator` (`up` or `down`).
- `chart`: `title`, `chartType` (`line`, `bar`, `stacked-bar`, `pie`, `donut`), `labels`, `series` entries with `name` and numeric `values`, optional `xAxis` and `yAxis`.
- `table`: `columns` with `key`, `label`, optional .NET numeric `format`; `rows` as objects; optional `totals` object and `highlightCells` entries with zero-based `row` and column `key`.
- `image`: base64 PNG/JPEG bytes in `base64`, optional `caption`.
- `callout`: `text`.
- `page-break`: omit `content` or pass an empty object.

All requested outputs render in memory before FileContext stores any file. When configured, `MaximumGeneratedFileBytes` applies to the combined size of the outputs. Hosts can also set `MaximumReportInputBytes`, `MaximumReportPages`, and an `OperationTimeout` for rendering and upload. PDF headings can continue onto additional pages, and KPI cards flow onto additional rows and pages as needed. PDF chart and table rows are kept intact across pages, table headers repeat after a page break, and each page has a title, NYWD mark or theme logo, UTC date, and page number. Failures name the block index and type where possible.

Named template and theme persistence, network allowlists, operator authorization, and authenticated download links belong to the host. `AIAgents.App` implements that boundary in its `AgentFiles/Reports` module.
