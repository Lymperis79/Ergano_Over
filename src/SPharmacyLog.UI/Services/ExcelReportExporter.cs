using System.IO.Compression;
using System.Xml.Linq;

namespace SPharmacyLog.UI.Services;

/// <summary>
/// Writes a lightweight, standards-compliant XLSX workbook without an external spreadsheet dependency.
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
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")))));

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
                    new XAttribute("Target", "worksheets/sheet1.xml")))));

        var rows = new List<XElement>();
        rows.Add(CreateRow(1, CreateCell("A1", "Report"), CreateCell("B1", reportName)));
        rows.Add(CreateRow(2, CreateCell("A2", "Generated"), CreateCell("B2", DateTime.Now.ToString("yyyy-MM-dd HH:mm"))));
        rows.Add(CreateRow(3, CreateCell("A3", "Summary"), CreateCell("B3", summary)));
        rows.Add(CreateRow(5, CreateCell("A5", "Type"), CreateCell("B5", "Details")));

        var rowNumber = 6;
        foreach (var line in details)
        {
            rows.Add(CreateRow(rowNumber,
                CreateCell($"A{rowNumber}", "Report detail"),
                CreateCell($"B{rowNumber}", line ?? string.Empty)));
            rowNumber++;
        }

        var worksheet = new XDocument(new XElement(MainNs + "worksheet",
            new XElement(MainNs + "cols",
                new XElement(MainNs + "col", new XAttribute("min", "1"), new XAttribute("max", "1"), new XAttribute("width", "20"), new XAttribute("customWidth", "1")),
                new XElement(MainNs + "col", new XAttribute("min", "2"), new XAttribute("max", "2"), new XAttribute("width", "100"), new XAttribute("customWidth", "1"))),
            new XElement(MainNs + "sheetData", rows)));
        WriteEntry(archive, "xl/worksheets/sheet1.xml", worksheet);

        return Task.CompletedTask;
    }

    private static XElement CreateRow(int index, params XElement[] cells) =>
        new(MainNs + "row", new XAttribute("r", index), cells);

    private static XElement CreateCell(string reference, string value) =>
        new(MainNs + "c",
            new XAttribute("r", reference),
            new XAttribute("t", "inlineStr"),
            new XElement(MainNs + "is",
                new XElement(MainNs + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value)));

    private static void WriteEntry(ZipArchive archive, string path, XDocument document)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
        document.Save(writer, SaveOptions.DisableFormatting);
    }
}
