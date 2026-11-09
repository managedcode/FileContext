using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace ManagedCode.FileContext.Tests;

public sealed class CsvModuleEdgeTests
{
    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    public void Auto_detected_delimiter_and_multiline_header_survive_supported_boms(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        var csv = "schema\r\n\"Назва\r\n🧪\";Count\r\n\"a\r\nb\";3\r\n";
        using var source = new MemoryStream([.. encoding.GetPreamble(), .. encoding.GetBytes(csv)]);

        var result = FileContextTableReader.Read(source, "supplies.csv", headerRow: 2);

        result.Delimiter.ShouldBe(";");
        var table = result.Tables.ShouldHaveSingleItem();
        table.HeaderRow.ShouldBe(2);
        table.Headers.ShouldBe(["Назва\r\n🧪", "Count"]);
        table.RowCount.ShouldBe(1);
    }

    [Fact]
    public void Quoted_crlf_across_the_buffer_counts_as_one_physical_line()
    {
        var csv = "\"" + new string('x', 4_094) + "\r\nlast\",Name\r\n\"bad\"tail,value";
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var error = Should.Throw<MalformedLineException>(() =>
            FileContextTableReader.Read(source, "supplies.csv", delimiter: ","));

        error.LineNumber.ShouldBe(3);
    }
}
