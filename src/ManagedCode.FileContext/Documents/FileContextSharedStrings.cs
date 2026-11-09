using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ManagedCode.FileContext;

internal sealed class FileContextSharedStrings(
    SharedStringTablePart? table,
    CancellationToken token,
    bool singleCell = false)
{
    private int _selectedIndex = -1;
    private string? _selectedValue;
    private string[]? _allValues;

    public string Get(int index)
    {
        if (singleCell && _selectedIndex < 0)
        {
            _selectedValue = ReadSelected(index);
            _selectedIndex = index;
        }

        if (index == _selectedIndex) { return _selectedValue!; }

        // A worksheet may have repeated coordinates. Read the full table once
        // if a single-cell query encounters another shared-string index.
        var values = _allValues ??= table?.SharedStringTable?.Elements<SharedStringItem>()
            .Select(FileContextWorkbookReader.ReadBaseText).ToArray() ?? [];
        return index < values.Length ? values[index] : throw InvalidReference();
    }

    private string ReadSelected(int index)
    {
        if (table is null) { throw InvalidReference(); }

        using var reader = OpenXmlReader.Create(table);
        if (!reader.Read() || reader.ElementType != typeof(SharedStringTable) || !reader.ReadFirstChild())
        {
            throw InvalidReference();
        }

        var current = 0;
        string? value = null;
        do
        {
            token.ThrowIfCancellationRequested();
            if (reader.IsStartElement && reader.ElementType == typeof(SharedStringItem) && current++ == index)
            {
                value = FileContextWorkbookReader.ReadBaseText(reader.LoadCurrentElement());
            }
        }
        while (reader.ReadNextSibling());

        // Reading through the part keeps XML validation and character limits
        // active even after the selected string was found.
        return value ?? throw InvalidReference();
    }

    public static InvalidDataException InvalidReference() =>
        new("Workbook cell contains an invalid shared-string reference.");
}
