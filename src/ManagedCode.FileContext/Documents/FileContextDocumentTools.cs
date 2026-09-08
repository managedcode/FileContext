using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace ManagedCode.FileContext;

/// <summary>Creates the same document functions for the context provider and host-owned native/JavaScript registries.</summary>
public static class FileContextDocumentTools
{
    public static IReadOnlyList<AIFunction> Create(ManagedCodeStorageFileStore store, FileContextOptions options)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (!options.EnableWriteTools) { return []; }
        var service = new FileContextDocumentService(store, options);
        AIFunction[] functions =
        [
            AIFunctionFactory.Create(service.CreateTextAsync, new AIFunctionFactoryOptions
            {
                Name = FileContextToolNames.CreateText,
                Description = "Create a new UTF-8 file in the scoped FileContext. Returns its unique relative path, media type and byte length. Existing files are never overwritten.",
            }),
            AIFunctionFactory.Create(service.CreateCsvAsync, new AIFunctionFactoryOptions
            {
                Name = FileContextToolNames.CreateCsv,
                Description = "Create a UTF-8 CSV from rows with comma, semicolon or tab delimiter. Quotes and newlines are escaped; values remain literal. Returns a new FileContext path.",
            }),
            AIFunctionFactory.Create(service.CreateWorkbookAsync, new AIFunctionFactoryOptions
            {
                Name = FileContextToolNames.CreateWorkbook,
                Description = "Create an Excel .xlsx workbook with named sheets and typed text, number, boolean or explicit formula cells. Excel calculates formulas on open. Returns a new FileContext path.",
            }),
            AIFunctionFactory.Create(service.CreatePdfAsync, new AIFunctionFactoryOptions
            {
                Name = FileContextToolNames.CreatePdf,
                Description = "Create a paginated PDF from an optional title and paragraphs with embedded Latin/Cyrillic font and line wrapping. Returns a new FileContext path.",
            }),
        ];
        return options.RequireWriteToolApproval
            ? functions.Select(static function => (AIFunction)new ApprovalRequiredAIFunction(function)).ToArray()
            : functions;
    }
}
