using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ClosedXML.Excel;
using ErganiManager.Core.Interfaces;

namespace ErganiManager.UI.Services;

/// <summary>
/// Builds one Excel workbook from the time summary report:
/// sheet 1 = scheduled vs actual hours (every day), sheet 2 = Saturdays, sheet 3 = Sundays,
/// then one overtime grid for the selected dates (employees x days, details in a comment on each cell),
/// then the Holidays report (employee leave grouped by category, plus the public holidays of the period).
/// All hour cells are real Excel durations (format [h]:mm), so they can be summed and filtered in Excel.
/// </summary>
public static class TimeWorkbookExporter
{
    private const string Duration = "[h]:mm";
    private const string SignedMinutes = "+0;-0;0";
    private const int ColumnCount = 8;
    private const int HeaderRow = 3;
    private const string WeekendGreen = "#C6E0B4";

    private static string T(string key) => Tr.Loc[key];

    public static void Save(string path, TimeSummaryReport report, DateOnly from, DateOnly to, IReadOnlyList<EmployeeDto> employees,
        TableReport holidays)
    {
        using var workbook = new XLWorkbook();
        var period = $"{from:dd/MM/yyyy} – {to:dd/MM/yyyy}";

        WriteSheet(workbook, T("Vm.Excel Sheet scheduled vs actual"), T("Vm.Excel Title scheduled vs actual"), period, report,
            _ => true);
        WriteSheet(workbook, T("Vm.Excel Sheet Saturday"), T("Vm.Excel Title Saturday"), period, report,
            d => d.Date.DayOfWeek == DayOfWeek.Saturday && IsWorkday(d));
        WriteSheet(workbook, T("Vm.Excel Sheet Sunday"), T("Vm.Excel Title Sunday"), period, report,
            d => d.Date.DayOfWeek == DayOfWeek.Sunday && IsWorkday(d));

        // One overtime grid covering exactly the selected dates.
        WriteOvertimeSheet(workbook, from, to, employees, report);
        WriteTableReportSheet(workbook, T("Vm.Excel Sheet holidays"), holidays, from, to);

        workbook.SaveAs(path);
    }

    /// <summary>A day counts for the Saturday/Sunday sheets when work was scheduled or really done.</summary>
    private static bool IsWorkday(DayTimeRow d) => (d.DeclaredHours ?? 0) > 0 || (d.ActualHours ?? 0) > 0;

    private static void WriteSheet(XLWorkbook workbook, string sheetName, string title, string period,
        TimeSummaryReport report, Func<DayTimeRow, bool> include)
    {
        var ws = workbook.Worksheets.Add(sheetName);

        ws.Cell(1, 1).Value = $"{title} — {period}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        var headers = new[]
        {
            T("Vm.Excel Employee"), T("Vm.Excel Date"), T("Vm.Excel Day"), T("Vm.Excel Scheduled"),
            T("Vm.Excel Scheduled hours"), T("Vm.Excel Actual in/out"), T("Vm.Excel Actual hours"), T("Vm.Excel Difference")
        };
        for (var c = 0; c < headers.Length; c++) ws.Cell(HeaderRow, c + 1).Value = headers[c];
        var header = ws.Range(HeaderRow, 1, HeaderRow, ColumnCount);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E78");

        var row = HeaderRow + 1;
        double grandScheduled = 0, grandActual = 0, grandDiff = 0;
        var grandDays = 0;

        foreach (var month in report.Months)
        {
            var days = month.Days.Where(include).OrderBy(d => d.Date).ToList();
            if (days.Count == 0) continue;

            foreach (var d in days)
            {
                ws.Cell(row, 1).Value = month.EmployeeName;
                ws.Cell(row, 2).Value = d.Date.ToDateTime(TimeOnly.MinValue);
                ws.Cell(row, 2).Style.NumberFormat.Format = "dd/mm/yyyy";
                ws.Cell(row, 3).Value = CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(d.Date.DayOfWeek);
                ws.Cell(row, 4).Value = d.DeclaredText;
                SetDuration(ws.Cell(row, 5), d.DeclaredHours);
                ws.Cell(row, 6).Value = d.Pairs.Count > 0 ? d.ActualText : string.Empty;
                SetDuration(ws.Cell(row, 7), d.ActualHours);
                if (d.DiffHours is { } diff)
                {
                    SetMinutes(ws.Cell(row, 8), diff);
                    if (d.IsLate) ws.Cell(row, 8).Style.Font.FontColor = XLColor.Red;
                }
                row++;
            }

            var scheduled = days.Sum(d => d.DeclaredHours ?? 0);
            var actual = days.Sum(d => d.ActualHours ?? 0);
            var difference = days.Sum(d => d.DiffHours ?? 0);
            WriteTotalRow(ws, row++, month.EmployeeName, $"{T("Vm.Excel Total")} {month.Month:00}/{month.Year}",
                $"{days.Count} {T("Vm.Excel days")}", scheduled, actual, difference);

            grandScheduled += scheduled; grandActual += actual; grandDiff += difference; grandDays += days.Count;
        }

        if (row == HeaderRow + 1)
        {
            ws.Cell(row, 1).Value = T("Vm.Excel No rows");
            ws.Cell(row, 1).Style.Font.Italic = true;
        }
        else
        {
            WriteTotalRow(ws, row, string.Empty, T("Vm.Excel Grand total"), $"{grandDays} {T("Vm.Excel days")}",
                grandScheduled, grandActual, grandDiff);
        }

        ws.SheetView.FreezeRows(HeaderRow);
        ws.Columns().AdjustToContents(HeaderRow, row);
        ws.Column(1).Width = Math.Max(ws.Column(1).Width, 24);
    }

    /// <summary>
    /// Overtime grid for the selected dates: one row per employee, one column per day, weekends in green.
    /// Cell = the registered overtime of that day in minutes (the same records as the Overtime report).
    /// A comment on the cell shows the scans, the overtime and the rounded times.
    /// </summary>
    private static void WriteOvertimeSheet(XLWorkbook workbook, DateOnly first, DateOnly last,
        IReadOnlyList<EmployeeDto> employees, TimeSummaryReport report)
    {
        // Sheet names allow no slashes: 01.01.26-17.10.26
        var ws = workbook.Worksheets.Add($"{T("Vm.Excel Sheet overtime")} {first:dd.MM.yy}-{last:dd.MM.yy}");

        // All days of every month of the selected range, per employee.
        var daysByEmployee = report.Months
            .GroupBy(m => m.EmployeeId)
            .ToDictionary(g => g.Key,
                g => g.SelectMany(m => m.Days).GroupBy(d => d.Date).ToDictionary(x => x.Key, x => x.First()));

        ws.Cell(1, 1).Value = $"{first.Day}/{first.Month}/{first.Year} - {last.Day}/{last.Month}/{last.Year}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(2, 1).Value = T("Vm.Excel Names");
        ws.Cell(2, 2).Value = T("Vm.Excel Total");
        ws.Range(2, 1, 2, 2).Style.Font.Bold = true;

        var dayCount = last.DayNumber - first.DayNumber + 1;
        var dayTotals = new double[dayCount];
        for (var i = 0; i < dayCount; i++)
        {
            var header = ws.Cell(2, 3 + i);
            header.Value = first.AddDays(i).ToDateTime(TimeOnly.MinValue);
            header.Style.NumberFormat.Format = "d/m/yyyy";
            header.Style.Font.Bold = true;
        }

        var row = 3;
        foreach (var employee in employees)
        {
            var hasData = daysByEmployee.TryGetValue(employee.Id, out var days);
            if (!employee.IsActive && !hasData) continue;
            days ??= new Dictionary<DateOnly, DayTimeRow>();

            ws.Cell(row, 1).Value = string.IsNullOrWhiteSpace(employee.TaxId)
                ? $"{employee.LastName} {employee.FirstName}"
                : $"{employee.TaxId} - {employee.LastName} {employee.FirstName}";

            double total = 0;
            for (var i = 0; i < dayCount; i++)
            {
                if (!days.TryGetValue(first.AddDays(i), out var day)) continue;

                var minutes = Math.Round(day.OvertimeHours * 60);
                if (minutes < 1) continue;

                var cell = ws.Cell(row, 3 + i);
                cell.Value = minutes;
                cell.Style.NumberFormat.Format = "0.00";
                AddOvertimeComment(cell, day);

                total += minutes;
                dayTotals[i] += minutes;
            }

            ws.Cell(row, 2).Value = total;
            ws.Cell(row, 2).Style.NumberFormat.Format = "0.00";
            row++;
        }

        // Bottom total row (sum per day and overall).
        var lastEmployeeRow = row - 1;
        if (lastEmployeeRow >= 3)
        {
            ws.Cell(row, 1).Value = T("Vm.Excel Total");
            ws.Cell(row, 2).Value = dayTotals.Sum();
            ws.Cell(row, 2).Style.NumberFormat.Format = "0.00";
            for (var i = 0; i < dayCount; i++)
            {
                if (dayTotals[i] <= 0) continue;
                ws.Cell(row, 3 + i).Value = dayTotals[i];
                ws.Cell(row, 3 + i).Style.NumberFormat.Format = "0.00";
            }
        }
        else
        {
            ws.Cell(row, 1).Value = T("Vm.Excel No rows");
            ws.Cell(row, 1).Style.Font.Italic = true;
        }

        // Weekend columns in green (header + all rows), then style the total row on top.
        var lastRow = Math.Max(row, 3);
        for (var i = 0; i < dayCount; i++)
        {
            if (first.AddDays(i).DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                ws.Range(2, 3 + i, lastRow, 3 + i).Style.Fill.BackgroundColor = XLColor.FromHtml(WeekendGreen);
        }
        if (lastEmployeeRow >= 3)
        {
            var totalRange = ws.Range(row, 1, row, 2 + dayCount);
            totalRange.Style.Font.Bold = true;
            totalRange.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        }

        ws.Column(1).Width = 40;
        ws.Column(2).Width = 10;
        for (var i = 0; i < dayCount; i++) ws.Column(3 + i).Width = 11;
    }

    /// <summary>The yellow balloon: scans, registered overtime, rounded out time and rounded worked time.</summary>
    private static void AddOvertimeComment(IXLCell cell, DayTimeRow day)
    {
        var inOut = string.Join(" | ", day.Pairs.Select(p =>
            $"{(p.In is { } i ? i.ToString("HH:mm") : "?")} - {(p.Out is { } o ? o.ToString("HH:mm") : "?")}"));
        var overtime = day.OvertimeHours;
        var modOut = day.RoundedPairs.Select(p => p.Out).LastOrDefault(x => x != null);
        var worked = day.RoundedHours ?? day.ActualHours;

        var lines = new List<string>
        {
            $"In - Out: {inOut}",
            $"Hours: {overtime.ToString("0.00", CultureInfo.CurrentCulture)}",
            $"TotTime: {HoursMinutes(overtime)}",
            $"Mod Out Time: {(modOut is { } m ? m.ToString("HH:mm") : string.Empty)}",
            $"Tot work MH: {HoursMinutes(worked ?? 0)}"
        };
        if (!string.IsNullOrEmpty(day.OvertimeRanges))
            lines.Add($"{T("Vm.Excel Registered overtime")}: {day.OvertimeRanges}");

        var comment = cell.CreateComment();
        comment.AddText(string.Join("\n", lines));
        comment.Style.Alignment.SetAutomaticSize();
    }

    /// <summary>Hours as zero-padded hh:mm (1.1667 → 01:10).</summary>
    private static string HoursMinutes(double hours)
    {
        var totalMinutes = (int)Math.Round(Math.Max(0, hours) * 60);
        return $"{totalMinutes / 60:00}:{totalMinutes % 60:00}";
    }

    /// <summary>
    /// Writes a generic table report (title, summary, then each section with its header and rows) to a sheet.
    /// Group rows become shaded category headings; whole numbers are stored as real numbers.
    /// </summary>
    private static void WriteTableReportSheet(XLWorkbook workbook, string sheetName, TableReport report,
        DateOnly from, DateOnly to)
    {
        var ws = workbook.Worksheets.Add(sheetName);
        ws.Cell(1, 1).Value = $"{report.Title} — {from:dd/MM/yyyy} – {to:dd/MM/yyyy}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(2, 1).Value = report.Summary;
        ws.Cell(2, 1).Style.Font.Italic = true;

        var row = 4;
        var widths = new Dictionary<int, double>();

        foreach (var section in report.Sections)
        {
            if (section.HasTitle)
            {
                ws.Cell(row, 1).Value = section.Title;
                ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Cell(row, 1).Style.Font.FontSize = 12;
                row++;
            }

            var columns = section.Columns;
            for (var c = 0; c < columns.Count; c++)
            {
                ws.Cell(row, c + 1).Value = columns[c].Header;
                widths[c + 1] = Math.Max(widths.GetValueOrDefault(c + 1), Math.Min(columns[c].Width / 6.5, 60));
            }
            StyleHeader(ws.Range(row, 1, row, Math.Max(1, columns.Count)));
            row++;

            foreach (var r in section.Rows)
            {
                if (r.IsGroup)
                {
                    ws.Cell(row, 1).Value = r.Cells.FirstOrDefault()?.Text ?? string.Empty;
                    var group = ws.Range(row, 1, row, Math.Max(1, columns.Count));
                    group.Style.Font.Bold = true;
                    group.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9E1F2");
                }
                else
                {
                    for (var c = 0; c < r.Cells.Count; c++)
                    {
                        var text = r.Cells[c].Text;
                        var cell = ws.Cell(row, c + 1);
                        if (text.Length is > 0 and < 7 && text.All(char.IsAsciiDigit)) cell.Value = int.Parse(text, CultureInfo.InvariantCulture);
                        else cell.Value = text;
                        if (r.Cells[c].IsWarn) cell.Style.Font.FontColor = XLColor.Red;
                    }
                    if (r.IsTotal)
                    {
                        var total = ws.Range(row, 1, row, Math.Max(1, columns.Count));
                        total.Style.Font.Bold = true;
                        total.Style.Border.TopBorder = XLBorderStyleValues.Thin;
                    }
                }
                row++;
            }

            if (section.Rows.Count == 0 && section.HasNote)
            {
                ws.Cell(row, 1).Value = section.DisplayNote;
                ws.Cell(row, 1).Style.Font.Italic = true;
                row++;
            }
            row++;   // blank line between sections
        }

        foreach (var (column, width) in widths) ws.Column(column).Width = Math.Max(width, 10);
    }

    private static void StyleHeader(IXLRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Font.FontColor = XLColor.White;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E78");
    }

    private static void WriteTotalRow(IXLWorksheet ws, int row, string employee, string label, string days,
        double scheduled, double actual, double difference)
    {
        ws.Cell(row, 1).Value = employee;
        ws.Cell(row, 2).Value = label;
        ws.Cell(row, 3).Value = days;
        SetDuration(ws.Cell(row, 5), scheduled);
        SetDuration(ws.Cell(row, 7), actual);
        SetMinutes(ws.Cell(row, 8), difference);

        var range = ws.Range(row, 1, row, ColumnCount);
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9E1F2");
    }

    /// <summary>Stores hours as an Excel duration (fraction of a day) so [h]:mm shows 8:30 and sums work.</summary>
    private static void SetDuration(IXLCell cell, double? hours)
    {
        if (hours is not { } h) return;
        cell.Value = h / 24.0;
        cell.Style.NumberFormat.Format = Duration;
    }

    /// <summary>
    /// Difference as whole signed minutes (+20 / -35). Excel cannot show negative durations, and a plain
    /// number format keeps the file valid everywhere while the cells stay summable.
    /// </summary>
    private static void SetMinutes(IXLCell cell, double hours)
    {
        cell.Value = Math.Round(hours * 60);
        cell.Style.NumberFormat.Format = SignedMinutes;
    }
}
