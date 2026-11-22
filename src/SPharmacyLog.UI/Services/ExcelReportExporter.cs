using System.IO.Compression;
using System.Xml.Linq;

namespace SPharmacyLog.UI.Services;

/// <summary>
/// Writes a formatted XLSX report without an external spreadsheet dependency.
/// </summary>
public static class ExcelReportExporter
{
    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    public static Task WriteAsync(Stream destination, string reportName, string summary, IReadOnlyList<string> details)
    {
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

        WriteEntry(archive, "[Content_Types].xml",
            new XDocument(new XElement(ContentTypeNs + "Types",
                new XElement(ContentTypeNs + "Default", new XAttribute("Extension", "rels"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(ContentTypeNs + "Default", new XAttribute("Extension", "xml"),
                    new XAttribute("ContentType", "application/xml")),
                new XElement(ContentTypeNs + "Override", new XAttribute("PartName", "/xl/workbook.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                new XElement(ContentTypeNs + "Override", new XAttribute("PartName", "/xl/worksheets/sheet1.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")),
                new XElement(ContentTypeNs + "Override", new XAttribute("PartName", "/xl/styles.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")))));

        WriteEntry(archive, "_rels/.rels",
            new XDocument(new XElement(PackageRelNs + "Relationships",
                new XElement(PackageRelNs + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "xl/workbook.xml")))));

        WriteEntry(archive, "xl/workbook.xml",
            new XDocument(new XElement(MainNs + "workbook",
                new XAttribute(XNamespace.Xmlns + "r", RelNs),
                new XElement(MainNs + "sheets",
                    new XElement(MainNs + "sheet",
                        new XAttribute("name", "Report"),
                        new XAttribute("sheetId", "1"),
                        new XAttribute(RelNs + "id", "rId1"))))));

        WriteEntry(archive, "xl/_rels/workbook.xml.rels",
            new XDocument(new XElement(PackageRelNs + "Relationships",
                new XElement(PackageRelNs + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                    new XAttribute("Target", "worksheets/sheet1.xml")),
                new XElement(PackageRelNs + "Relationship",
                    new XAttribute("Id", "rId2"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
                    new XAttribute("Target", "styles.xml")))));

        var rows = new List<XElement>
        {
            CreateRow(1, 30, CreateCell("A1", "Report", 1), CreateCell("B1", reportName, 1)),
            CreateRow(2, 22, CreateCell("A2", "Generated", 2), CreateCell("B2", DateTime.Now.ToString("yyyy-MM-dd HH:mm"), 3)),
            CreateRow(3, 48, CreateCell("A3", "Summary", 2), CreateCell("B3", summary, 4)),
            CreateRow(5, 24, CreateCell("A5", "Report details", 5))
        };

        var rowNumber = 6;
        foreach (var rawLine in details)
        {
            var line = rawLine?.TrimEnd() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(line) || line.Trim().All(character => character == '='))
                continue;

            var trimmed = line.Trim();
            var isSection = trimmed.EndsWith(':');
            var isIndented = rawLine.StartsWith("  ", StringComparison.Ordinal);
            var style = isSection ? 5 : isIndented ? 7 : 6;
            var height = Math.Clamp(18 + (line.Length / 105) * 15, 18, 90);
            rows.Add(CreateRow(rowNumber, height, CreateCell($"A{rowNumber}", trimmed, style)));
            rowNumber++;
        }

        var lastRow = Math.Max(5, rowNumber - 1);
        var worksheet = new XDocument(new XElement(MainNs + "worksheet",
            new XElement(MainNs + "sheetViews",
                new XElement(MainNs + "sheetView", new XAttribute("workbookViewId", "0"),
                    new XElement(MainNs + "pane",
                        new XAttribute("ySplit", "5"),
                        new XAttribute("topLeftCell", "A6"),
                        new XAttribute("activePane", "bottomLeft"),
                        new XAttribute("state", "frozen")))),
            new XElement(MainNs + "cols",
                new XElement(MainNs + "col", new XAttribute("min", "1"), new XAttribute("max", "1"), new XAttribute("width", "105"), new XAttribute("customWidth", "1")),
                new XElement(MainNs + "col", new XAttribute("min", "2"), new XAttribute("max", "2"), new XAttribute("width", "55"), new XAttribute("customWidth", "1"))),
            new XElement(MainNs + "sheetData", rows),
            new XElement(MainNs + "autoFilter", new XAttribute("ref", $"A5:A{lastRow}"))));
        WriteEntry(archive, "xl/worksheets/sheet1.xml", worksheet);
        WriteEntry(archive, "xl/styles.xml", CreateStyles());

        return Task.CompletedTask;
    }

    private static XDocument CreateStyles() =>
        new(new XElement(MainNs + "styleSheet",
            new XElement(MainNs + "fonts",
                new XAttribute("count", "3"),
                new XElement(MainNs + "font",
                    new XElement(MainNs + "sz", new XAttribute("val", "11")),
                    new XElement(MainNs + "name", new XAttribute("val", "Aptos"))),
                new XElement(MainNs + "font",
                    new XElement(MainNs + "b"),
                    new XElement(MainNs + "sz", new XAttribute("val", "16")),
                    new XElement(MainNs + "color", new XAttribute("rgb", "FFFFFFFF")),
                    new XElement(MainNs + "name", new XAttribute("val", "Aptos Display"))),
                new XElement(MainNs + "font",
                    new XElement(MainNs + "b"),
                    new XElement(MainNs + "sz", new XAttribute("val", "11")),
                    new XElement(MainNs + "color", new XAttribute("rgb", "FF344054")),
                    new XElement(MainNs + "name", new XAttribute("val", "Aptos")))),
            new XElement(MainNs + "fills",
                new XAttribute("count", "4"),
                new XElement(MainNs + "fill", new XElement(MainNs + "patternFill", new XAttribute("patternType", "none"))),
                new XElement(MainNs + "fill", new XElement(MainNs + "patternFill", new XAttribute("patternType", "gray125"))),
                new XElement(MainNs + "fill", new XElement(MainNs + "patternFill", new XAttribute("patternType", "solid"),
                    new XElement(MainNs + "fgColor", new XAttribute("rgb", "FF24476A")),
                    new XElement(MainNs + "bgColor", new XAttribute("indexed", "64")))),
                new XElement(MainNs + "fill", new XElement(MainNs + "patternFill", new XAttribute("patternType", "solid"),
                    new XElement(MainNs + "fgColor", new XAttribute("rgb", "FFEAF1F8")),
                    new XElement(MainNs + "bgColor", new XAttribute("indexed", "64"))))),
            new XElement(MainNs + "borders",
                new XAttribute("count", "2"),
                new XElement(MainNs + "border",
                    new XElement(MainNs + "left"), new XElement(MainNs + "right"),
                    new XElement(MainNs + "top"), new XElement(MainNs + "bottom"),
                    new XElement(MainNs + "diagonal")),
                new XElement(MainNs + "border",
                    new XElement(MainNs + "left", new XAttribute("style", "thin"), new XElement(MainNs + "color", new XAttribute("rgb", "FFD5DEE8"))),
                    new XElement(MainNs + "right", new XAttribute("style", "thin"), new XElement(MainNs + "color", new XAttribute("rgb", "FFD5DEE8"))),
                    new XElement(MainNs + "top", new XAttribute("style", "thin"), new XElement(MainNs + "color", new XAttribute("rgb", "FFD5DEE8"))),
                    new XElement(MainNs + "bottom", new XAttribute("style", "thin"), new XElement(MainNs + "color", new XAttribute("rgb", "FFD5DEE8"))),
                    new XElement(MainNs + "diagonal"))),
            new XElement(MainNs + "cellStyleXfs",
                new XAttribute("count", "1"),
                new XElement(MainNs + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "0"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"))),
            new XElement(MainNs + "cellXfs",
                new XAttribute("count", "8"),
                new XElement(MainNs + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "0"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"), new XAttribute("xfId", "0")),
                new XElement(MainNs + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "1"), new XAttribute("fillId", "2"), new XAttribute("borderId", "0"), new XAttribute("xfId", "0"), new XAttribute("applyAlignment", "1"),
                    new XElement(MainNs + "alignment", new XAttribute("vertical", "center"))),
                new XElement(MainNs + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "2"), new XAttribute("fillId", "3"), new XAttribute("borderId", "0"), new XAttribute("xfId", "0"), new XAttribute("applyAlignment", "1"),
                    new XElement(MainNs + "alignment", new XAttribute("vertical", "center"))),
                new XElement(MainNs + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "0"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"), new XAttribute("xfId", "0"), new XAttribute("applyAlignment", "1"),
                    new XElement(MainNs + "alignment", new XAttribute("vertical", "center"))),
                new XElement(MainNs + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "0"), new XAttribute("fillId", "3"), new XAttribute("borderId", "1"), new XAttribute("xfId", "0"), new XAttribute("applyAlignment", "1"),
                    new XElement(MainNs + "alignment", new XAttribute("vertical", "top"), new XAttribute("wrapText", "1"))),
                new XElement(MainNs + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "1"), new XAttribute("fillId", "2"), new XAttribute("borderId", "0"), new XAttribute("xfId", "0"), new XAttribute("applyAlignment", "1"),
                    new XElement(MainNs + "alignment", new XAttribute("vertical", "center"))),
                new XElement(MainNs + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "0"), new XAttribute("fillId", "0"), new XAttribute("borderId", "1"), new XAttribute("xfId", "0"), new XAttribute("applyAlignment", "1"),
                    new XElement(MainNs + "alignment", new XAttribute("vertical", "top"), new XAttribute("wrapText", "1"))),
                new XElement(MainNs + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "0"), new XAttribute("fillId", "3"), new XAttribute("borderId", "1"), new XAttribute("xfId", "0"), new XAttribute("applyAlignment", "1"),
                    new XElement(MainNs + "alignment", new XAttribute("vertical", "top"), new XAttribute("wrapText", "1")))),
            new XElement(MainNs + "cellStyles",
                new XAttribute("count", "1"),
                new XElement(MainNs + "cellStyle", new XAttribute("name", "Normal"), new XAttribute("xfId", "0"), new XAttribute("builtinId", "0")))));

    private static XElement CreateRow(int index, int height, params XElement[] cells) =>
        new(MainNs + "row", new XAttribute("r", index), new XAttribute("ht", height), new XAttribute("customHeight", "1"), cells);

    private static XElement CreateCell(string reference, string value, int styleIndex) =>
        new(MainNs + "c",
            new XAttribute("r", reference),
            new XAttribute("s", styleIndex),
            new XAttribute("t", "inlineStr"),
            new XElement(MainNs + "is",
                new XElement(MainNs + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value ?? string.Empty)));

    private static void WriteEntry(ZipArchive archive, string path, XDocument document)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
        document.Save(writer, SaveOptions.DisableFormatting);
    }
}
