using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ManagedCode.FileContext;

internal sealed class FileContextWorkbookStyles(WorkbookPart workbook)
{
    private const uint SystemBackgroundColor = 64;
    private const uint LastBuiltInNumberFormat = 163;
    private const int MaximumColumns = 16384;
    private const int MaximumColumnWidth = 255;
    private const int HexColorLength = 7;
    private readonly Dictionary<(bool Bold, string? Fill, string? Format), uint> cache = [];

    public uint Style(FileContextWorksheetStyle style, int row, int column)
    {
        var key = (row == 0 && style.BoldHeader, row == 0 ? style.HeaderFill : null,
            row == 0 ? null : style.NumberFormats.GetValueOrDefault(column));
        if (key == (false, null, null)) { return 0; }
        if (cache.TryGetValue(key, out var existing)) { return existing; }
        var sheet = EnsureStyles();
        uint font = 0, fill = 0, format = 0;
        if (key.Item1)
        {
            font = (uint)sheet.Fonts!.ChildElements.Count;
            sheet.Fonts.AppendChild(new Font { Bold = new Bold() });
        }
        if (key.Item2 is { } color)
        {
            if (color.Length != HexColorLength || color[0] != '#' || color.Skip(1).Any(c => !Uri.IsHexDigit(c)))
            { throw new ArgumentException("HeaderFill must be #RRGGBB.", nameof(style)); }
            fill = (uint)sheet.Fills!.ChildElements.Count;
            sheet.Fills.AppendChild(new Fill
            {
                PatternFill = new PatternFill
                {
                    ForegroundColor = new ForegroundColor { Rgb = "FF" + color[1..] },
                    BackgroundColor = new BackgroundColor { Indexed = SystemBackgroundColor },
                    PatternType = PatternValues.Solid
                }
            });
        }
        if (key.Item3 is { } code)
        {
            if (string.IsNullOrWhiteSpace(code)) { throw new ArgumentException("Number format cannot be empty.", nameof(style)); }
            format = Math.Max(LastBuiltInNumberFormat, sheet.NumberingFormats!.Elements<NumberingFormat>()
                .Select(item => item.NumberFormatId!.Value).DefaultIfEmpty(LastBuiltInNumberFormat).Max()) + 1;
            sheet.NumberingFormats.AppendChild(new NumberingFormat { NumberFormatId = format, FormatCode = code });
        }
        var index = (uint)sheet.CellFormats!.ChildElements.Count;
        sheet.CellFormats.AppendChild(new CellFormat
        {
            FontId = font,
            FillId = fill,
            BorderId = 0,
            FormatId = 0,
            NumberFormatId = format,
            ApplyFont = key.Item1,
            ApplyFill = fill != 0,
            ApplyNumberFormat = format != 0
        });
        sheet.Fonts!.Count = (uint)sheet.Fonts.ChildElements.Count;
        sheet.Fills!.Count = (uint)sheet.Fills.ChildElements.Count;
        sheet.NumberingFormats!.Count = (uint)sheet.NumberingFormats.ChildElements.Count;
        sheet.CellFormats.Count = (uint)sheet.CellFormats.ChildElements.Count;
        cache[key] = index;
        return index;
    }

    public static void Layout(Worksheet sheet, FileContextWorksheetStyle style)
    {
        if (style.NumberFormats.Keys.Concat(style.ColumnWidths.Keys).Any(column => column is < 1 or > MaximumColumns))
        { throw new ArgumentException("Style columns must be one-based Excel columns 1 through 16384.", nameof(style)); }
        if (style.FreezeHeader)
        {
            var views = sheet.GetFirstChild<SheetViews>() ?? sheet.InsertAt(new SheetViews(), 0);
            var view = views.Elements<SheetView>().FirstOrDefault() ?? views.AppendChild(new SheetView { WorkbookViewId = 0 });
            view.RemoveAllChildren<Pane>();
            view.PrependChild(new Pane { VerticalSplit = 1, TopLeftCell = "A2", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen });
        }
        if (style.ColumnWidths.Count == 0) { return; }
        var columns = sheet.GetFirstChild<Columns>();
        if (columns is null)
        {
            columns = new Columns();
            sheet.InsertBefore(columns, sheet.GetFirstChild<SheetData>());
        }
        foreach (var (column, width) in style.ColumnWidths.OrderBy(pair => pair.Key))
        {
            if (!double.IsFinite(width) || width <= 0 || width > MaximumColumnWidth)
            { throw new ArgumentException("Column widths must be positive and at most Excel's 255 characters.", nameof(style)); }
            // A template's overlapping range is split so unrelated widths remain intact.
            foreach (var prior in columns.Elements<Column>().Where(item => item.Min!.Value <= column && item.Max!.Value >= column).ToArray())
            {
                if (prior.Min!.Value < column)
                { var left = (Column)prior.CloneNode(true); left.Max = (uint)column - 1; columns.InsertBefore(left, prior); }
                if (prior.Max!.Value > column)
                { var right = (Column)prior.CloneNode(true); right.Min = (uint)column + 1; columns.InsertAfter(right, prior); }
                prior.Remove();
            }
            columns.AppendChild(new Column { Min = (uint)column, Max = (uint)column, Width = width, CustomWidth = true });
        }
    }

    private Stylesheet EnsureStyles()
    {
        var part = workbook.WorkbookStylesPart ?? workbook.AddNewPart<WorkbookStylesPart>();
        var sheet = part.Stylesheet ??= new Stylesheet();
        if (sheet.NumberingFormats is null) { sheet.NumberingFormats = new NumberingFormats(); }
        if (sheet.Fonts is null) { sheet.Fonts = new Fonts(); sheet.Fonts.AppendChild(new Font()); }
        if (sheet.Fills is null) { sheet.Fills = new Fills(new Fill { PatternFill = new PatternFill { PatternType = PatternValues.None } }, new Fill { PatternFill = new PatternFill { PatternType = PatternValues.Gray125 } }); }
        if (sheet.Borders is null) { sheet.Borders = new Borders(); sheet.Borders.AppendChild(new Border()); }
        if (sheet.CellStyleFormats is null) { sheet.CellStyleFormats = new CellStyleFormats(); sheet.CellStyleFormats.AppendChild(new CellFormat()); }
        if (sheet.CellFormats is null) { sheet.CellFormats = new CellFormats(); sheet.CellFormats.AppendChild(new CellFormat()); }
        return sheet;
    }
}
