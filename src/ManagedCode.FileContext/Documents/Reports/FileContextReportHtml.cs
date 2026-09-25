// Block validation reports the relevant JSON field in the message.
#pragma warning disable MA0015
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ManagedCode.FileContext;

internal static partial class FileContextReportHtml
{
    private const int MinimumKpis = 2;
    private const int MaximumKpis = 6;
    private const int ListMarkerLength = 2;
    private static readonly Regex Bold = new(@"\*\*(?<text>.+?)\*\*", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex Link = new(@"\[(?<text>[^\]]+)\]\((?<url>https://[^\s)]+)\)", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    public static string Render(FileContextReport report, FileContextReportTheme theme, CancellationToken token)
    {
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; img-src data:; style-src 'unsafe-inline'\">")
            .Append("<title>").Append(E(report.Title)).Append("</title><style>")
            .Append(Style(theme, report)).Append("</style></head><body><header>");
        if (theme.LogoBase64 is { Length: > 0 })
        {
            html.Append("<img class=\"mark\" alt=\"NYWD logo\" src=\"data:image/png;base64,")
                .Append(theme.LogoBase64).Append("\">");
        }
        else { html.Append("<span class=\"mark\">NY</span>"); }
        html.Append("<strong>")
            .Append(E(report.Title)).Append("</strong></header><main>");
        for (var index = 0; index < report.Blocks.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            try { Block(html, report.Blocks[index], theme, token); }
            catch (Exception error) when (error is not OperationCanceledException)
            { throw new ArgumentException($"blocks[{index}] ({report.Blocks[index].Type}) failed: {error.Message}", error); }
        }
        html.Append("</main><footer>NYWD · ").Append(DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))
            .Append(" <span class=\"page\"></span></footer></body></html>");
        return html.ToString();
    }

    private static void Block(StringBuilder html, FileContextReportBlock block, FileContextReportTheme theme, CancellationToken token)
    {
        var content = block.Content;
        switch (block.Type)
        {
            case "heading":
                html.Append("<section class=\"heading\"><h1>").Append(E(ReportJson.Text(content, "title")))
                    .Append("</h1><p>").Append(E(ReportJson.Text(content, "subtitle"))).Append("</p></section>");
                break;
            case "text":
                html.Append("<section class=\"prose\">");
                foreach (var paragraph in ReportJson.Text(content, "text").Split("\n\n"))
                { html.Append("<p>").Append(Formatted(paragraph)).Append("</p>"); }
                html.Append("</section>");
                break;
            case "kpi-row": Kpis(html, content); break;
            case "chart":
                html.Append("<figure class=\"chart\"><img alt=\"").Append(E(ReportJson.Text(content, "title", "Chart")))
                    .Append("\" src=\"data:image/png;base64,").Append(Convert.ToBase64String(FileContextReportChart.RenderPng(content, theme, token)))
                    .Append("\"><figcaption>").Append(E(ReportJson.Text(content, "title"))).Append("</figcaption></figure>");
                break;
            case "table": Table(html, content); break;
            case "image":
                var image = ReportJson.Text(content, "base64");
                var bytes = Convert.FromBase64String(image);
                var mime = FileContextReportImage.Validate(bytes);
                html.Append("<figure><img alt=\"").Append(E(ReportJson.Text(content, "caption")))
                    .Append("\" src=\"data:").Append(mime).Append(";base64,").Append(image)
                    .Append("\"><figcaption>").Append(E(ReportJson.Text(content, "caption"))).Append("</figcaption></figure>");
                break;
            case "callout":
                html.Append("<aside>").Append(Formatted(ReportJson.Text(content, "text"))).Append("</aside>");
                break;
            case "page-break": html.Append("<div class=\"page-break\"></div>"); break;
        }
    }

    private static void Kpis(StringBuilder html, JsonElement content)
    {
        var items = ReportJson.Array(content, "items");
        if (items.Count < MinimumKpis || items.Count > MaximumKpis) { throw new ArgumentException("kpi-row.items requires 2 to 6 cards."); }
        html.Append("<section class=\"kpis\">");
        foreach (var item in items)
        {
            var indicator = ReportJson.Text(item, "indicator");
            var trend = ReportJson.Text(item, "change");
            if (indicator is "up") { trend = "↑ " + trend; }
            else if (indicator is "down") { trend = "↓ " + trend; }
            html.Append("<div class=\"kpi\"><small>").Append(E(ReportJson.Text(item, "label"))).Append("</small><b>")
                .Append(E(ReportJson.Text(item, "value"))).Append("</b><em>")
                .Append(E(trend)).Append("</em></div>");
        }
        html.Append("</section>");
    }

    private static void Table(StringBuilder html, JsonElement content)
    {
        var columns = ReportJson.Array(content, "columns");
        if (columns.Count == 0) { throw new ArgumentException("table.columns is required."); }
        html.Append("<table><thead><tr>");
        foreach (var column in columns)
        { html.Append("<th>").Append(E(ReportJson.Text(column, "label", ReportJson.Text(column, "key")))).Append("</th>"); }
        html.Append("</tr></thead><tbody>");
        var rowIndex = 0;
        foreach (var row in ReportJson.Array(content, "rows"))
        {
            html.Append("<tr>");
            for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                var column = columns[columnIndex];
                var highlighted = ReportJson.IsHighlighted(content, rowIndex, ReportJson.Text(column, "key"));
                html.Append(highlighted ? "<td class=\"highlight\">" : "<td>")
                    .Append(E(ReportJson.FormatCell(row, column))).Append("</td>");
            }
            html.Append("</tr>");
            rowIndex++;
        }
        html.Append("</tbody>");
        var totals = ReportJson.Property(content, "totals");
        if (totals.ValueKind == JsonValueKind.Object)
        {
            html.Append("<tfoot><tr>");
            foreach (var column in columns)
            { html.Append("<td>").Append(E(ReportJson.FormatCell(totals, column))).Append("</td>"); }
            html.Append("</tr></tfoot>");
        }
        html.Append("</table>");
    }

    private static string Formatted(string raw)
    {
        var html = new StringBuilder();
        var inList = false;
        foreach (var line in raw.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var bullet = line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal);
            if (bullet && !inList) { html.Append("<ul>"); inList = true; }
            if (!bullet && inList) { html.Append("</ul>"); inList = false; }
            if (bullet) { html.Append("<li>").Append(Inline(line[ListMarkerLength..])).Append("</li>"); }
            else { html.Append(Inline(line)).Append("<br>"); }
        }
        if (inList) { html.Append("</ul>"); }
        return html.ToString();
    }

    private static string Inline(string raw)
    {
        var encoded = E(raw);
        encoded = Bold.Replace(encoded, "<strong>${text}</strong>");
        return Link.Replace(encoded, match => $"<a href=\"{match.Groups["url"].Value}\" rel=\"noreferrer\">{match.Groups["text"].Value}</a>");
    }

    private static string Style(FileContextReportTheme theme, FileContextReport report) =>
        """
        :root{--primary:$PRIMARY$;--accent:$ACCENT$;--text:$TEXT$;--muted:$MUTED$}
        *{box-sizing:border-box}body{margin:0;background:#f1f4f7;color:var(--text);font:15px/1.5 Arial,sans-serif}
        header,main,footer{max-width:1000px;margin:auto;background:white;padding:24px 42px}
        header{display:flex;align-items:center;gap:14px;border-bottom:4px solid var(--accent);color:var(--primary);font-size:20px}
        .mark{display:inline-grid;place-items:center;width:38px;height:38px;background:var(--accent);color:white;font-weight:bold;font-size:15px}
        main{min-height:70vh;padding-top:34px;padding-bottom:40px}.heading h1{font-size:28px;margin:0;color:var(--primary)}
        .heading p{margin:4px 0 24px;color:var(--muted)}.prose{margin:18px 0 24px}.prose p{margin:0 0 12px}
        .kpis{display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:12px;margin:22px 0}
        .kpi{background:#f0f4f8;border-radius:8px;padding:16px;display:flex;flex-direction:column;break-inside:avoid}
        .kpi small{color:var(--muted)}.kpi b{font-size:26px;color:var(--primary)}.kpi em{font-style:normal;color:var(--muted)}
        figure{margin:24px 0;break-inside:avoid}figure img{width:100%;height:auto;max-height:480px;object-fit:contain}
        figcaption{font-size:12px;color:var(--muted)}table{width:100%;border-collapse:collapse;margin:22px 0;font-size:13px}
        th{text-align:left;background:var(--primary);color:white}th,td{padding:9px 10px;border-bottom:1px solid #e3e8ee}
        tr{break-inside:avoid}tbody tr:nth-child(even){background:#f8fafc}tfoot{font-weight:bold;background:#e8edf3}td.highlight{background:#f9f2da;color:var(--primary);font-weight:bold}
        aside{background:#f9f2da;border-left:4px solid var(--accent);padding:16px;margin:20px 0;break-inside:avoid}
        footer{border-top:1px solid #d6dce3;color:var(--muted);font-size:12px}.page-break{break-before:page}
        @media print{body{background:white}header,main,footer{max-width:none}thead{display:table-header-group}
        footer{position:fixed;bottom:0;left:0;right:0}.page:after{content:'Page ' counter(page)}
        @page{size:$PAGESIZE$ $ORIENTATION$;margin:14mm}}
        """.Replace("$PRIMARY$", E(theme.Primary), StringComparison.Ordinal)
        .Replace("$ACCENT$", E(theme.Accent), StringComparison.Ordinal)
        .Replace("$TEXT$", E(theme.Text), StringComparison.Ordinal)
        .Replace("$MUTED$", E(theme.Muted), StringComparison.Ordinal)
        .Replace("$PAGESIZE$", report.PageSize, StringComparison.Ordinal)
        .Replace("$ORIENTATION$", report.Orientation, StringComparison.Ordinal);

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
