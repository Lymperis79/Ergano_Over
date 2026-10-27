using System;
using ClosedXML.Excel;
using ErganiManager.Core.Interfaces;

namespace ErganiManager.UI.Services;

/// <summary>Exports the custom-range attendance report to a two-sheet Excel workbook.</summary>
public static class AttendanceWorkbookExporter
{
    private const int HeaderRow = 3;
    private const string DurationFormat = "[h]:mm";

    private static string T(string key) => Tr.Loc[key];

    public static void Save(string path, AttendanceReport report, DateOnly from, DateOnly to)
    {
        using var workbook = new XLWorkbook();
        var period = $"{from:dd/MM/yyyy} – {to:dd/MM/yyyy}";

        var detail = workbook.Worksheets.Add(T("Vm.Attendance detail sheet"));
        detail.Cell(1, 1).Value = $"{T("Vm.Attendance and punctuality")} — {period}";
        detail.Cell(1, 1).Style.Font.Bold = true;
        detail.Cell(1, 1).Style.Font.FontSize = 14;

        var headers = new[]
        {
            T("Vm.Employee"), T("Vm.Date"), T("Vm.Weekday"), T("Vm.Schedule type"),
            T("Vm.Scheduled start"), T("Vm.Scheduled end"), T("Vm.Scheduled hours"),
            T("Vm.First arrival"), T("Vm.Last departure"), T("Vm.Actual hours"),
            T("Vm.Late minutes"), T("Vm.Early departure minutes"), T("Vm.Attendance status"),
            T("Vm.Scan pairs"), T("Vm.Declared holiday"), T("Vm.Holiday type")
        };
        for (var c = 0; c < headers.Length; c++) detail.Cell(HeaderRow, c + 1).Value = headers[c];
        StyleHeader(detail.Range(HeaderRow, 1, HeaderRow, headers.Length));

        var row = HeaderRow + 1;
        foreach (var item in report.Rows.OrderBy(x => x.EmployeeName, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Date))
        {
            detail.Cell(row, 1).Value = item.EmployeeName;
            detail.Cell(row, 2).Value = item.Date.ToDateTime(TimeOnly.MinValue);
            detail.Cell(row, 2).Style.NumberFormat.Format = "dd/mm/yyyy";
            detail.Cell(row, 3).Value = item.Date.ToDateTime(TimeOnly.MinValue).ToString("dddd");
            detail.Cell(row, 4).Value = item.ScheduleKind.Length == 0 ? T("Vm.No schedule") : T("Vm." + item.ScheduleKind);
            if (item.ScheduledStart is { } start) detail.Cell(row, 5).Value = start.ToTimeSpan().TotalDays;
            if (item.ScheduledEnd is { } end) detail.Cell(row, 6).Value = end.ToTimeSpan().TotalDays;
            detail.Cell(row, 5).Style.NumberFormat.Format = "hh:mm";
            detail.Cell(row, 6).Style.NumberFormat.Format = "hh:mm";
            SetDuration(detail.Cell(row, 7), item.ScheduledHours);
            if (item.FirstArrival is { } arrival) detail.Cell(row, 8).Value = arrival;
            if (item.LastDeparture is { } departure) detail.Cell(row, 9).Value = departure;
            detail.Cell(row, 8).Style.NumberFormat.Format = "dd/mm/yyyy hh:mm";
            detail.Cell(row, 9).Style.NumberFormat.Format = "dd/mm/yyyy hh:mm";
            SetDuration(detail.Cell(row, 10), item.ActualHours);
            if (item.LateMinutes > 0) detail.Cell(row, 11).Value = item.LateMinutes;
            if (item.EarlyDepartureMinutes > 0) detail.Cell(row, 12).Value = item.EarlyDepartureMinutes;
            detail.Cell(row, 13).Value = item.Status;
            detail.Cell(row, 14).Value = item.ScanPairs;
            detail.Cell(row, 15).Value = item.IsDeclaredHoliday ? T("Yes") : string.Empty;
            detail.Cell(row, 16).Value = item.HolidayType;
            if (item.IsDeclaredHoliday)
            {
                detail.Cell(row, 15).Style.Font.Bold = true;
                detail.Cell(row, 15).Style.Font.FontColor = XLColor.DarkGreen;
                detail.Cell(row, 16).Style.Alignment.WrapText = true;
            }
            if (item.Status == ErganiManager.Data.UiText.T("No show") ||
                item.Status == ErganiManager.Data.UiText.T("Missing clock-in") ||
                item.Status == ErganiManager.Data.UiText.T("Missing clock-out"))
                detail.Cell(row, 13).Style.Font.FontColor = XLColor.Red;
            if (item.LateMinutes > 0) detail.Cell(row, 11).Style.Font.FontColor = XLColor.Red;
            row++;
        }

        if (row == HeaderRow + 1)
        {
            detail.Cell(row, 1).Value = T("Vm.Excel No rows");
        }
        detail.SheetView.FreezeRows(HeaderRow);
        detail.Range(HeaderRow, 1, Math.Max(HeaderRow, row - 1), headers.Length).SetAutoFilter();
        detail.Columns().AdjustToContents(1, Math.Max(row, HeaderRow));
        detail.Column(1).Width = Math.Max(detail.Column(1).Width, 22);
        detail.Column(13).Width = Math.Max(detail.Column(13).Width, 25);
        detail.Column(14).Width = Math.Max(detail.Column(14).Width, 32);
        detail.Column(15).Width = Math.Max(detail.Column(15).Width, 18);
        detail.Column(16).Width = Math.Max(detail.Column(16).Width, 32);

        var summary = workbook.Worksheets.Add(T("Vm.Attendance summary sheet"));
        summary.Cell(1, 1).Value = $"{T("Vm.Attendance employee summary")} — {period}";
        summary.Cell(1, 1).Style.Font.Bold = true;
        summary.Cell(1, 1).Style.Font.FontSize = 14;
        var summaryHeaders = new[]
        {
            T("Vm.Employee"), T("Vm.Scheduled days"), T("Vm.Present days"), T("Vm.No-show days"),
            T("Vm.Late days"), T("Vm.Missing clock-in days"), T("Vm.Missing clock-out days"), T("Vm.Actual hours")
        };
        for (var c = 0; c < summaryHeaders.Length; c++) summary.Cell(HeaderRow, c + 1).Value = summaryHeaders[c];
        StyleHeader(summary.Range(HeaderRow, 1, HeaderRow, summaryHeaders.Length));
        row = HeaderRow + 1;
        foreach (var item in report.EmployeeSummaries)
        {
            summary.Cell(row, 1).Value = item.EmployeeName;
            summary.Cell(row, 2).Value = item.ScheduledDays;
            summary.Cell(row, 3).Value = item.PresentDays;
            summary.Cell(row, 4).Value = item.NoShowDays;
            summary.Cell(row, 5).Value = item.LateDays;
            summary.Cell(row, 6).Value = item.MissingClockInDays;
            summary.Cell(row, 7).Value = item.MissingClockOutDays;
            summary.Cell(row, 8).Value = item.ActualHours / 24d;
            summary.Cell(row, 8).Style.NumberFormat.Format = DurationFormat;
            row++;
        }

        summary.Cell(row + 1, 1).Value = T("Vm.Report totals");
        summary.Cell(row + 1, 1).Style.Font.Bold = true;
        summary.Cell(row + 1, 2).Value = report.ScheduledDays;
        summary.Cell(row + 1, 3).Value = report.PresentDays;
        summary.Cell(row + 1, 4).Value = report.NoShowDays;
        summary.Cell(row + 1, 5).Value = report.LateDays;
        summary.Cell(row + 1, 6).Value = report.MissingClockInDays;
        summary.Cell(row + 1, 7).Value = report.MissingClockOutDays;
        summary.Cell(row + 1, 8).Value = report.ActualHours / 24d;
        summary.Cell(row + 1, 8).Style.NumberFormat.Format = DurationFormat;
        summary.Range(row + 1, 1, row + 1, summaryHeaders.Length).Style.Font.Bold = true;
        summary.SheetView.FreezeRows(HeaderRow);
        summary.Range(HeaderRow, 1, Math.Max(HeaderRow, row - 1), summaryHeaders.Length).SetAutoFilter();
        summary.Columns().AdjustToContents(1, row + 1);
        summary.Column(1).Width = Math.Max(summary.Column(1).Width, 22);

        workbook.SaveAs(path);
    }

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
