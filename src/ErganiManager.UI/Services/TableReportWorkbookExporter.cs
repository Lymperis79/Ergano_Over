using System;
using System.Linq;
using ClosedXML.Excel;
using ErganiManager.Core.Interfaces;

namespace ErganiManager.UI.Services;

/// <summary>Exports the generic analysis tables to a multi-sheet Excel workbook.</summary>
public static class TableReportWorkbookExporter
{
    public static void Save(string path, TableReport report, DateOnly from, DateOnly to)
    {
        using var workbook = new XLWorkbook();
        var title = string.IsNullOrWhiteSpace(report.Title) ? "Analysis report" : report.Title;
        var period = $"{from:dd/MM/yyyy} – {to:dd/MM/yyyy}";
        var usedNames = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var section in report.Sections)
        {
            var baseName = CleanSheetName(string.IsNullOrWhiteSpace(section.Title) ? title : section.Title);
            var sheetName = baseName;
            var suffix = 2;
            while (!usedNames.Add(sheetName))
            {
                var suffixText = $" ({suffix++})";
                sheetName = baseName[..Math.Min(baseName.Length, 31 - suffixText.Length)] + suffixText;
            }

            var ws = workbook.Worksheets.Add(sheetName);
            ws.Cell(1, 1).Value = title;
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(2, 1).Value = period;
            ws.Cell(3, 1).Value = section.Title;
            ws.Cell(3, 1).Style.Font.Bold = true;
            ws.Cell(4, 1).Value = report.Summary;
            ws.Cell(4, 1).Style.Alignment.WrapText = true;
            var headerRow = 6;
            for (var c = 0; c < section.Columns.Count; c++)
                ws.Cell(headerRow, c + 1).Value = section.Columns[c].Header;

            if (section.Columns.Count > 0)
            {
                var header = ws.Range(headerRow, 1, headerRow, section.Columns.Count);
                header.Style.Font.Bold = true;
                header.Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
                header.Style.Alignment.WrapText = true;
            }

            var row = headerRow + 1;
            foreach (var dataRow in section.Rows)
            {
                for (var c = 0; c < dataRow.Cells.Count && c < section.Columns.Count; c++)
                    ws.Cell(row, c + 1).Value = dataRow.Cells[c].Text;
                if (dataRow.Style == RowStyle.Total)
                    ws.Range(row, 1, row, Math.Max(1, section.Columns.Count)).Style.Font.Bold = true;
                else if (dataRow.Style == RowStyle.Warning)
                    ws.Range(row, 1, row, Math.Max(1, section.Columns.Count)).Style.Font.FontColor = XLColor.DarkOrange;
                row++;
            }

            if (section.Columns.Count > 0 && row > headerRow + 1)
                ws.Range(headerRow, 1, row - 1, section.Columns.Count).SetAutoFilter();
            if (!string.IsNullOrWhiteSpace(section.Note))
            {
                ws.Cell(row + 1, 1).Value = section.Note;
                ws.Cell(row + 1, 1).Style.Alignment.WrapText = true;
            }
            ws.SheetView.FreezeRows(headerRow);
            ws.Columns(1, Math.Max(1, section.Columns.Count)).AdjustToContents(1, Math.Max(row, headerRow));
            if (section.Columns.Count > 0)
                for (var c = 1; c <= section.Columns.Count; c++)
                    if (ws.Column(c).Width > 42) ws.Column(c).Width = 42;
        }

        if (workbook.Worksheets.Count == 0)
        {
            var ws = workbook.Worksheets.Add("Analysis");
            ws.Cell(1, 1).Value = title;
            ws.Cell(2, 1).Value = period;
            ws.Cell(4, 1).Value = report.Summary;
            ws.Column(1).Width = 100;
            ws.Cell(4, 1).Style.Alignment.WrapText = true;
        }
        workbook.SaveAs(path);
    }

    private static string CleanSheetName(string name)
    {
        var cleaned = new string(name.Where(c => c is not ('[' or ']' or ':' or '*' or '?' or '/' or '\\')).ToArray()).Trim();
        if (cleaned.Length == 0) cleaned = "Report";
        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }
}
