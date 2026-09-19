using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using ErganiManager.Core.Interfaces;

namespace ErganiManager.UI.Services;

public class EmployeeImportRow
{
    public int RowNumber { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string TaxId { get; set; } = string.Empty;
    public string SocialSecurityNumber { get; set; } = string.Empty;
    public string BarcodeId { get; set; } = string.Empty;
    public string ProfessionCode { get; set; } = string.Empty;
    public int WeeklyWorkdays { get; set; } = 5;
    public string BranchName { get; set; } = string.Empty;

    public bool IsValid { get; set; }
    public string? Error { get; set; }
}

public class ExcelImportResult
{
    public List<EmployeeImportRow> ValidRows { get; set; } = new();
    public List<EmployeeImportRow> InvalidRows { get; set; } = new();
    public int TotalRows { get; set; }
}

public class ExcelImportExportService
{
    // ── EMPLOYEE IMPORT ───────────────────────────────────────────────────────

    public static void GenerateEmployeeImportTemplate(string filePath)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Employees");

        // Headers
        var headers = new[]
        {
            "FirstName", "LastName", "TaxId (AFM)", "SocialSecurityNumber (AMKA)",
            "BarcodeId", "ProfessionCode", "WeeklyWorkdays", "BranchName", "IsActive"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
            ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.FromArgb(0x1E, 0x21, 0x28);
            ws.Cell(1, i + 1).Style.Font.FontColor = XLColor.White;
        }

        // One example row
        ws.Cell(2, 1).Value = "Antonis";
        ws.Cell(2, 2).Value = "Papadopoulos";
        ws.Cell(2, 3).Value = "123456789";
        ws.Cell(2, 4).Value = "12345678901";
        ws.Cell(2, 5).Value = "EMP001";
        ws.Cell(2, 6).Value = "251101";
        ws.Cell(2, 7).Value = 5;
        ws.Cell(2, 8).Value = "Main Branch";
        ws.Cell(2, 9).Value = "TRUE";

        ws.Columns().AdjustToContents();
        wb.SaveAs(filePath);
    }

    public static ExcelImportResult ParseEmployeeImportFile(string filePath)
    {
        var result = new ExcelImportResult();

        using var wb = new XLWorkbook(filePath);
        var ws = wb.Worksheets.First();
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

        // Start from row 2 (skip header)
        for (int row = 2; row <= lastRow; row++)
        {
            var importRow = new EmployeeImportRow { RowNumber = row };

            importRow.FirstName = ws.Cell(row, 1).GetString().Trim();
            importRow.LastName = ws.Cell(row, 2).GetString().Trim();
            importRow.TaxId = ws.Cell(row, 3).GetString().Trim();
            importRow.SocialSecurityNumber = ws.Cell(row, 4).GetString().Trim();
            importRow.BarcodeId = ws.Cell(row, 5).GetString().Trim();
            importRow.ProfessionCode = ws.Cell(row, 6).GetString().Trim();
            importRow.BranchName = ws.Cell(row, 8).GetString().Trim();

            var weeklyDaysStr = ws.Cell(row, 7).GetString().Trim();
            importRow.WeeklyWorkdays = int.TryParse(weeklyDaysStr, out var wd) ? wd : 5;

            // Validate
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(importRow.FirstName)) errors.Add("First name required");
            if (string.IsNullOrWhiteSpace(importRow.LastName)) errors.Add("Last name required");
            if (importRow.TaxId.Length != 9) errors.Add("AFM must be 9 digits");
            if (importRow.SocialSecurityNumber.Length != 11) errors.Add("AMKA must be 11 digits");
            if (string.IsNullOrWhiteSpace(importRow.BarcodeId)) errors.Add("Barcode ID required");
            if (importRow.WeeklyWorkdays is not (5 or 6)) errors.Add("Weekly workdays must be 5 or 6");
            if (string.IsNullOrWhiteSpace(importRow.BranchName)) errors.Add("Branch name required");

            importRow.IsValid = errors.Count == 0;
            importRow.Error = errors.Count > 0 ? string.Join("; ", errors) : null;

            result.TotalRows++;
            if (importRow.IsValid)
                result.ValidRows.Add(importRow);
            else
                result.InvalidRows.Add(importRow);
        }

        return result;
    }

    // ── WORK CARD HISTORY EXPORT ──────────────────────────────────────────────

    public static string ExportWorkCardHistory(List<WorkCardHistoryDto> records, string folderPath)
    {
        var fileName = $"WorkCardHistory_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        var filePath = Path.Combine(folderPath, fileName);

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Work Cards");

        // Header row
        var headers = new[]
        {
            "Employee", "Tax ID (AFM)", "Branch", "Type",
            "Date", "Time", "Submitted", "Protocol",
            "Early Departure", "Early (min)", "Email Alert Sent"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0x2D, 0x9C, 0xDB);
            cell.Style.Font.FontColor = XLColor.White;
        }

        // Data rows
        for (int i = 0; i < records.Count; i++)
        {
            var r = records[i];
            int row = i + 2;

            ws.Cell(row, 1).Value = r.EmployeeFullName;
            ws.Cell(row, 2).Value = r.EmployeeTaxId;
            ws.Cell(row, 3).Value = r.BranchName;
            ws.Cell(row, 4).Value = r.MovementType;
            ws.Cell(row, 5).Value = r.MovementDateTime.ToString("dd/MM/yyyy");
            ws.Cell(row, 6).Value = r.MovementDateTime.ToString("HH:mm:ss");
            ws.Cell(row, 7).Value = r.SubmittedToErgani ? "Yes" : "No";
            ws.Cell(row, 8).Value = r.Protocol ?? "";
            ws.Cell(row, 9).Value = r.WasEarlyDeparture ? "Yes" : "No";
            ws.Cell(row, 10).Value = r.EarlyDepartureMinutes?.ToString() ?? "";
            ws.Cell(row, 11).Value = r.EmailAlertSent ? "Yes" : "No";

            // Highlight early departures in amber
            if (r.WasEarlyDeparture)
            {
                ws.Range(row, 1, row, headers.Length)
                  .Style.Fill.BackgroundColor = XLColor.FromArgb(0x4E, 0x34, 0x2E);
            }
        }

        // Freeze header row and auto-fit columns
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();

        // Auto-filter
        ws.RangeUsed()?.SetAutoFilter();

        wb.SaveAs(filePath);
        return filePath;
    }

    // ── SCHEDULE EXPORT ────────────────────────────────────────────────────────

    public static string ExportMonthSchedule(
        string employeeFullName,
        int year, int month,
        List<ScheduleDayDto> schedules,
        string folderPath)
    {
        var fileName = $"Schedule_{employeeFullName.Replace(" ", "_")}_{year}{month:D2}.xlsx";
        var filePath = Path.Combine(folderPath, fileName);

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet($"{new DateTime(year, month, 1):MMMM yyyy}");

        // Header
        ws.Cell(1, 1).Value = $"Schedule: {employeeFullName}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Range("A1:G1").Merge();

        var dayHeaders = new[] { "Date", "Day", "Work Type", "Start", "End", "Submitted", "Protocol" };
        for (int i = 0; i < dayHeaders.Length; i++)
        {
            var cell = ws.Cell(3, i + 1);
            cell.Value = dayHeaders[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0x1E, 0x21, 0x28);
            cell.Style.Font.FontColor = XLColor.White;
        }

        var scheduleByDate = schedules.ToDictionary(s => s.ScheduleDate);
        var daysInMonth = DateTime.DaysInMonth(year, month);

        for (int day = 1; day <= daysInMonth; day++)
        {
            var date = new DateOnly(year, month, day);
            int row = day + 3;

            ws.Cell(row, 1).Value = date.ToString("dd/MM/yyyy");
            ws.Cell(row, 2).Value = date.ToString("ddd");

            if (scheduleByDate.TryGetValue(date, out var sched))
            {
                ws.Cell(row, 3).Value = sched.WorkType.ToString();
                ws.Cell(row, 4).Value = sched.StartTime?.ToString("HH:mm") ?? "";
                ws.Cell(row, 5).Value = sched.EndTime?.ToString("HH:mm") ?? "";
                ws.Cell(row, 6).Value = sched.SubmittedToErgani ? "Yes" : "No";
                ws.Cell(row, 7).Value = sched.Protocol ?? "";
            }
            else
            {
                ws.Cell(row, 3).Value = "—";
                for (int c = 4; c <= 7; c++)
                    ws.Cell(row, c).Value = "";
            }

            // Weekend shading
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                ws.Range(row, 1, row, 7).Style.Fill.BackgroundColor = XLColor.FromArgb(0x23, 0x26, 0x2D);
        }

        ws.Columns().AdjustToContents();
        wb.SaveAs(filePath);
        return filePath;
    }

    // ── Schedule Import ───────────────────────────────────────────────────────

    public static List<ScheduleImportRow> ParseScheduleImportFile(string filePath)
    {
        using var wb = new XLWorkbook(filePath);
        var ws   = wb.Worksheets.First();
        var rows = new List<ScheduleImportRow>();
        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

        for (int r = 2; r <= lastRow; r++)
        {
            var dateCell = ws.Cell(r, 1);
            if (dateCell.IsEmpty()) continue;

            DateOnly date;
            if (dateCell.DataType == XLDataType.DateTime)
                date = DateOnly.FromDateTime(dateCell.GetDateTime());
            else if (DateOnly.TryParse(dateCell.GetValue<string>(), out var parsed))
                date = parsed;
            else
                continue;

            var workTypeStr = ws.Cell(r, 2).GetValue<string>().Trim().ToLowerInvariant();
            var workType = workTypeStr switch
            {
                "office" or "work" => (AppWorkType?)AppWorkType.Office,
                "home"             => AppWorkType.Home,
                "rest"             => AppWorkType.Rest,
                "absent"           => AppWorkType.Absent,
                _                  => null
            };
            if (workType == null) continue;

            TimeOnly? start = null, end = null;
            if (TimeOnly.TryParse(ws.Cell(r, 3).GetValue<string>(), out var s)) start = s;
            if (TimeOnly.TryParse(ws.Cell(r, 4).GetValue<string>(), out var e)) end   = e;

            var comments = ws.Cell(r, 5).GetValue<string>().Trim();
            rows.Add(new ScheduleImportRow
            {
                Date      = date,
                WorkType  = workType.Value,
                StartTime = start,
                EndTime   = end,
                Comments  = string.IsNullOrEmpty(comments) ? null : comments
            });
        }
        return rows;
    }
    public static string ExportEmployees(
        IReadOnlyList<ErganiManager.Core.Interfaces.EmployeeDto> employees,
        IReadOnlyDictionary<int, string> branchNames,
        string folderPath)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Employees");

        // Header
        var headers = new[]
        {
            "Last Name", "First Name", "Tax ID (AFM)", "AMKA",
            "Barcode ID", "Profession Code", "Weekly Workdays",
            "Branch", "Active"
        };

        for (int c = 0; c < headers.Length; c++)
        {
            ws.Cell(1, c + 1).Value = headers[c];
            ws.Cell(1, c + 1).Style.Font.Bold = true;
            ws.Cell(1, c + 1).Style.Fill.BackgroundColor = XLColor.FromArgb(0x1E, 0x21, 0x28);
            ws.Cell(1, c + 1).Style.Font.FontColor = XLColor.White;
        }

        int row = 2;
        foreach (var e in employees)
        {
            branchNames.TryGetValue(e.BranchId, out var branchName);
            ws.Cell(row, 1).Value = e.LastName;
            ws.Cell(row, 2).Value = e.FirstName;
            ws.Cell(row, 3).Value = e.TaxId;
            ws.Cell(row, 4).Value = e.SocialSecurityNumber;
            ws.Cell(row, 5).Value = e.BarcodeId;
            ws.Cell(row, 6).Value = e.ProfessionCode;
            ws.Cell(row, 7).Value = e.WeeklyWorkdays;
            ws.Cell(row, 8).Value = branchName ?? string.Empty;
            ws.Cell(row, 9).Value = e.IsActive ? "Yes" : "No";

            if (!e.IsActive)
                ws.Range(row, 1, row, 9).Style.Font.FontColor = XLColor.Gray;

            row++;
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
        var filePath = Path.Combine(folderPath, $"Employees_{timestamp}.xlsx");
        wb.SaveAs(filePath);
        return filePath;
    }

    // ── Employee Export ───────────────────────────────────────────────────────
}

public class ScheduleImportRow
{
    public DateOnly    Date      { get; init; }
    public AppWorkType WorkType  { get; init; }
    public TimeOnly?   StartTime { get; init; }
    public TimeOnly?   EndTime   { get; init; }
    public string?     Comments  { get; init; }
}
