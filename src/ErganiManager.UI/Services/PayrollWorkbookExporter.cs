using System;
using System.Collections.Generic;
using System.Linq;
using ClosedXML.Excel;
using ErganiManager.Core.Interfaces;

namespace ErganiManager.UI.Services;

/// <summary>
/// Exports payroll-preparation figures only. It deliberately does not calculate wages,
/// premiums, deductions, or statutory amounts because those require employee-specific pay rules.
/// </summary>
public static class PayrollWorkbookExporter
{
    private const int HeaderRow = 3;
    private const string DurationFormat = "[h]:mm";
    private static string T(string key) => Tr.Loc[key];

    public static void Save(string path, TimeSummaryReport time, OvertimeReport overtime,
        DateOnly from, DateOnly to)
    {
        using var workbook = new XLWorkbook();
        var period = $"{from:dd/MM/yyyy} – {to:dd/MM/yyyy}";
        var overtimeByEmployeeMonth = overtime.Months
            .GroupBy(x => (x.EmployeeId, x.Year, x.Month))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.TotalHours));

        var summary = workbook.Worksheets.Add(SheetName(T("Vm.Monthly payroll preparation")));
        summary.Cell(1, 1).Value = $"{T("Vm.Monthly payroll preparation")} — {period}";
        summary.Cell(1, 1).Style.Font.Bold = true;
        summary.Cell(1, 1).Style.Font.FontSize = 14;
        summary.Cell(2, 1).Value = T("Vm.Payroll disclaimer");
        summary.Cell(2, 1).Style.Alignment.WrapText = true;
        summary.Row(2).Height = 32;

        var headers = new[]
        {
            T("Vm.Employee"), T("Vm.Month"), T("Vm.Scheduled hours"), T("Vm.Actual hours"),
            T("Vm.Difference (hours)"), T("Vm.Night hours"), T("Vm.Overtime hours"), T("Vm.Recorded overtime")
        };
        for (var c = 0; c < headers.Length; c++) summary.Cell(HeaderRow, c + 1).Value = headers[c];
        StyleHeader(summary.Range(HeaderRow, 1, HeaderRow, headers.Length));

        var row = HeaderRow + 1;
        foreach (var month in time.Months.OrderBy(x => x.EmployeeName, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(x => x.Year).ThenBy(x => x.Month))
        {
            summary.Cell(row, 1).Value = month.EmployeeName;
            summary.Cell(row, 2).Value = $"{month.Month:00}/{month.Year}";
            SetDuration(summary.Cell(row, 3), month.DeclaredHours);
            SetDuration(summary.Cell(row, 4), month.ActualHours);
            summary.Cell(row, 5).Value = month.DiffHours;
            summary.Cell(row, 5).Style.NumberFormat.Format = "0.00;[Red]-0.00";
            SetDuration(summary.Cell(row, 6), month.NightHours);
            SetDuration(summary.Cell(row, 7), month.OvertimeHours);
            var recorded = overtimeByEmployeeMonth.TryGetValue((month.EmployeeId, month.Year, month.Month), out var h) ? h : 0;
            SetDuration(summary.Cell(row, 8), recorded);
            if (month.DiffHours < -0.0833) summary.Cell(row, 5).Style.Font.FontColor = XLColor.Red;
            row++;
        }
        if (row == HeaderRow + 1) summary.Cell(row, 1).Value = T("Vm.Excel No rows");
        summary.SheetView.FreezeRows(HeaderRow);
        summary.Range(HeaderRow, 1, Math.Max(HeaderRow, row - 1), headers.Length).SetAutoFilter();
        summary.Columns().AdjustToContents(1, Math.Max(row, HeaderRow));
        summary.Column(1).Width = Math.Max(summary.Column(1).Width, 22);
        summary.Column(2).Width = Math.Max(summary.Column(2).Width, 12);

        var detail = workbook.Worksheets.Add(SheetName(T("Vm.Payroll daily detail")));
        detail.Cell(1, 1).Value = $"{T("Vm.Payroll daily detail")} — {period}";
        detail.Cell(1, 1).Style.Font.Bold = true;
        detail.Cell(1, 1).Style.Font.FontSize = 14;
        var detailHeaders = new[]
        {
            T("Vm.Employee"), T("Vm.Date"), T("Vm.Declared"), T("Vm.Declared hours"),
            T("Vm.Actual hours"), T("Vm.Difference (hours)"), T("Vm.Night hours"), T("Vm.Overtime hours"),
            T("Vm.Rounded hours"), T("Vm.Rounded in → out"), T("Vm.Scan pairs")
        };
        for (var c = 0; c < detailHeaders.Length; c++) detail.Cell(HeaderRow, c + 1).Value = detailHeaders[c];
        StyleHeader(detail.Range(HeaderRow, 1, HeaderRow, detailHeaders.Length));
        row = HeaderRow + 1;
        foreach (var month in time.Months.OrderBy(x => x.EmployeeName, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Year).ThenBy(x => x.Month))
        foreach (var day in month.Days.Where(d => d.Date >= from && d.Date <= to).OrderBy(d => d.Date))
        {
            detail.Cell(row, 1).Value = month.EmployeeName;
            detail.Cell(row, 2).Value = day.Date.ToDateTime(TimeOnly.MinValue);
            detail.Cell(row, 2).Style.NumberFormat.Format = "dd/mm/yyyy";
            detail.Cell(row, 3).Value = day.DeclaredText;
            SetDuration(detail.Cell(row, 4), day.DeclaredHours);
            SetDuration(detail.Cell(row, 5), day.ActualHours);
            if (day.DiffHours is { } difference) detail.Cell(row, 6).Value = difference;
            detail.Cell(row, 6).Style.NumberFormat.Format = "0.00;[Red]-0.00";
            SetDuration(detail.Cell(row, 7), day.NightHours);
            SetDuration(detail.Cell(row, 8), day.OvertimeHours);
            SetDuration(detail.Cell(row, 9), day.RoundedHours);
            detail.Cell(row, 10).Value = day.RoundedInOutCsvText;
            detail.Cell(row, 11).Value = string.Join(" | ", day.Pairs.Select(p => p.Text));
            if (day.DiffHours is { } dayDiff && dayDiff < -0.0833) detail.Cell(row, 6).Style.Font.FontColor = XLColor.Red;
            row++;
        }
        if (row == HeaderRow + 1) detail.Cell(row, 1).Value = T("Vm.Excel No rows");
        detail.SheetView.FreezeRows(HeaderRow);
        detail.Range(HeaderRow, 1, Math.Max(HeaderRow, row - 1), detailHeaders.Length).SetAutoFilter();
        detail.Columns().AdjustToContents(1, Math.Max(row, HeaderRow));
        detail.Column(1).Width = Math.Max(detail.Column(1).Width, 22);
        detail.Column(11).Width = Math.Max(detail.Column(11).Width, 32);

        var otSheet = workbook.Worksheets.Add(SheetName(T("Vm.Payroll overtime detail")));
        otSheet.Cell(1, 1).Value = $"{T("Vm.Payroll overtime detail")} — {period}";
        otSheet.Cell(1, 1).Style.Font.Bold = true;
        otSheet.Cell(1, 1).Style.Font.FontSize = 14;
        var otHeaders = new[] { T("Vm.Employee"), T("Vm.Date"), T("Vm.From"), T("Vm.To"), T("Vm.Hours"), T("Vm.Justification"), T("Vm.Sent to Ergani"), T("Vm.Protocol") };
        for (var c = 0; c < otHeaders.Length; c++) otSheet.Cell(HeaderRow, c + 1).Value = otHeaders[c];
        StyleHeader(otSheet.Range(HeaderRow, 1, HeaderRow, otHeaders.Length));
        row = HeaderRow + 1;
        foreach (var month in overtime.Months.OrderBy(x => x.EmployeeName, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Year).ThenBy(x => x.Month))
        foreach (var item in month.Records.OrderBy(x => x.OvertimeDate).ThenBy(x => x.StartTime))
        {
            otSheet.Cell(row, 1).Value = month.EmployeeName;
            otSheet.Cell(row, 2).Value = item.OvertimeDate.ToDateTime(TimeOnly.MinValue);
            otSheet.Cell(row, 2).Style.NumberFormat.Format = "dd/mm/yyyy";
            otSheet.Cell(row, 3).Value = item.StartTime.ToString("HH:mm");
            otSheet.Cell(row, 4).Value = item.EndTime.ToString("HH:mm");
            SetDuration(otSheet.Cell(row, 5), item.HoursWorked);
            otSheet.Cell(row, 6).Value = item.JustificationLabel;
            otSheet.Cell(row, 7).Value = item.SubmittedToErgani ? T("Vm.Yes") : T("Vm.No");
            otSheet.Cell(row, 8).Value = item.Protocol;
            row++;
        }
        if (row == HeaderRow + 1) otSheet.Cell(row, 1).Value = T("Vm.Excel No rows");
        otSheet.SheetView.FreezeRows(HeaderRow);
        otSheet.Range(HeaderRow, 1, Math.Max(HeaderRow, row - 1), otHeaders.Length).SetAutoFilter();
        otSheet.Columns().AdjustToContents(1, Math.Max(row, HeaderRow));
        otSheet.Column(1).Width = Math.Max(otSheet.Column(1).Width, 22);
        workbook.SaveAs(path);
    }

    private static string SheetName(string value) => value.Length <= 31 ? value : value[..31];

    private static void SetDuration(IXLCell cell, double? hours)
    {
        if (hours is not { } value) return;
        cell.Value = value / 24d;
        cell.Style.NumberFormat.Format = DurationFormat;
    }

    private static void StyleHeader(IXLRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = XLColor.LightGray;
        range.Style.Alignment.WrapText = true;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Worksheet.Row(range.FirstRow().RowNumber()).Height = 32;
    }
}
