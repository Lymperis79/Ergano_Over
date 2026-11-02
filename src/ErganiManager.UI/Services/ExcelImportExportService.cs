using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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
    public double NormalDailyHours { get; set; } = 8;
    public bool IsActive { get; set; } = true;
    public string FatherName { get; set; } = string.Empty;
    public string MotherName { get; set; } = string.Empty;
    public DateTimeOffset? BirthDate { get; set; }
    public string Sex { get; set; } = string.Empty;
    public string Nationality { get; set; } = string.Empty;
    public string MaritalStatus { get; set; } = string.Empty;
    public int? NumberOfChildren { get; set; }
    public string IdentityDocumentType { get; set; } = string.Empty;
    public string IdentityDocumentNumber { get; set; } = string.Empty;
    public DateTimeOffset? EmploymentStartDate { get; set; }
    public string EmploymentClassification { get; set; } = string.Empty;
    public string ExperienceYears { get; set; } = string.Empty;
    public string EmploymentRelationship { get; set; } = string.Empty;
    public string EmploymentStatus { get; set; } = string.Empty;
    public double? WeeklyHours { get; set; }
    public string WorkScheduleDescription { get; set; } = string.Empty;
    public string BreakDescription { get; set; } = string.Empty;
    public string EducationLevel { get; set; } = string.Empty;
    public string DigitalTimeOrganization { get; set; } = string.Empty;
    public double? FullEmploymentHours { get; set; }
    public int? BreakMinutes { get; set; }
    public string BreakWithinWorkingHours { get; set; } = string.Empty;
    public string UsesDigitalWorkCard { get; set; } = string.Empty;
    public int? FlexibleScheduleMinutes { get; set; }
    public DateTimeOffset? LastModifiedDate { get; set; }

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
        var headers = new[]
        {
            "FirstName", "LastName", "TaxId", "SocialSecurityNumber", "BarcodeId",
            "ProfessionCode", "WeeklyWorkdays", "BranchName", "IsActive", "NormalDailyHours",
            "FatherName", "MotherName", "BirthDate", "Sex", "Nationality", "MaritalStatus",
            "NumberOfChildren", "IdentityDocumentType", "IdentityDocumentNumber", "EmploymentStartDate",
            "EmploymentClassification", "ExperienceYears", "EmploymentRelationship", "EmploymentStatus",
            "WeeklyHours", "WorkScheduleDescription", "BreakDescription", "EducationLevel",
            "DigitalTimeOrganization", "FullEmploymentHours", "BreakMinutes",
            "BreakWithinWorkingHours", "UsesDigitalWorkCard", "FlexibleScheduleMinutes", "LastModifiedDate"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
            ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.FromArgb(0x1E, 0x21, 0x28);
            ws.Cell(1, i + 1).Style.Font.FontColor = XLColor.White;
        }

        ws.Cell(2, 1).Value = "Antonis";
        ws.Cell(2, 2).Value = "Papadopoulos";
        ws.Cell(2, 3).Value = "123456789";
        ws.Cell(2, 4).Value = "12345678901";
        ws.Cell(2, 5).Value = "EMP001";
        ws.Cell(2, 6).Value = "251101";
        ws.Cell(2, 7).Value = 5;
        ws.Cell(2, 8).Value = "Main Branch";
        ws.Cell(2, 9).Value = true;
        ws.Cell(2, 10).Value = 8;
        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);
        ws.RangeUsed()?.SetAutoFilter();
        wb.SaveAs(filePath);
    }

    public static ExcelImportResult ParseEmployeeImportFile(string filePath)
    {
        var result = new ExcelImportResult();
        using var wb = new XLWorkbook(filePath);
        var ws = wb.Worksheets.First();
        var headerRow = ws.FirstRowUsed()?.RowNumber() ?? 1;
        var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var cell in ws.Row(headerRow).CellsUsed())
        {
            var key = NormalizeHeader(cell.GetString()).Replace(" ", string.Empty);
            if (!string.IsNullOrWhiteSpace(key))
                headerMap[key] = cell.Address.ColumnNumber;
        }

        int Col(params string[] names)
        {
            foreach (var name in names)
            {
                var key = NormalizeHeader(name).Replace(" ", string.Empty);
                if (headerMap.TryGetValue(key, out var column))
                    return column;
            }
            return 0;
        }

        string Read(int row, params string[] names)
        {
            var col = Col(names);
            return col == 0 ? string.Empty : ws.Cell(row, col).GetString().Trim();
        }

        double? ReadDouble(int row, params string[] names)
        {
            var raw = Read(row, names);
            return double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out var value) ? value : null;
        }

        int? ReadInt(int row, params string[] names)
        {
            var raw = Read(row, names);
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
        }

        DateTimeOffset? ReadDate(int row, params string[] names)
        {
            var col = Col(names);
            if (col == 0 || ws.Cell(row, col).IsEmpty()) return null;
            var cell = ws.Cell(row, col);
            if (cell.DataType == XLDataType.DateTime)
                return new DateTimeOffset(DateTime.SpecifyKind(cell.GetDateTime(), DateTimeKind.Unspecified));
            return DateTimeOffset.TryParse(cell.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var value) ? value : null;
        }

        var firstCol = Col("FirstName", "First Name");
        var lastCol = Col("LastName", "Last Name");
        var afmCol = Col("TaxId", "Tax ID (AFM)", "AFM");
        var amkaCol = Col("SocialSecurityNumber", "SocialSecurityNumber (AMKA)", "AMKA");
        var barcodeCol = Col("BarcodeId", "Barcode ID");
        var branchCol = Col("BranchName", "Branch");
        if (firstCol == 0 || lastCol == 0 || afmCol == 0 || amkaCol == 0 ||
            barcodeCol == 0 || branchCol == 0)
            throw new InvalidDataException("The employee file is missing one or more required columns: FirstName, LastName, TaxId, SocialSecurityNumber, BarcodeId, BranchName.");

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow;
        for (int row = headerRow + 1; row <= lastRow; row++)
        {
            if (ws.Row(row).CellsUsed().All(cell => string.IsNullOrWhiteSpace(cell.GetString())))
                continue;

            var item = new EmployeeImportRow
            {
                RowNumber = row,
                FirstName = Read(row, "FirstName", "First Name"),
                LastName = Read(row, "LastName", "Last Name"),
                TaxId = DigitsOnly(Read(row, "TaxId", "Tax ID (AFM)", "AFM")),
                SocialSecurityNumber = DigitsOnly(Read(row, "SocialSecurityNumber", "SocialSecurityNumber (AMKA)", "AMKA")),
                BarcodeId = Read(row, "BarcodeId", "Barcode ID"),
                ProfessionCode = Read(row, "ProfessionCode", "Profession Code"),
                WeeklyWorkdays = ReadInt(row, "WeeklyWorkdays", "Weekly Workdays") ?? 5,
                BranchName = Read(row, "BranchName", "Branch"),
                NormalDailyHours = ReadDouble(row, "NormalDailyHours", "Normal Daily Hours") ?? 8,
                IsActive = !bool.TryParse(Read(row, "IsActive", "Active"), out var active) || active,
                FatherName = Read(row, "FatherName"),
                MotherName = Read(row, "MotherName"),
                BirthDate = ReadDate(row, "BirthDate"),
                Sex = Read(row, "Sex"),
                Nationality = Read(row, "Nationality"),
                MaritalStatus = Read(row, "MaritalStatus"),
                NumberOfChildren = ReadInt(row, "NumberOfChildren", "NumChildren"),
                IdentityDocumentType = Read(row, "IdentityDocumentType", "TyposTaytotitas"),
                IdentityDocumentNumber = Read(row, "IdentityDocumentNumber", "ArTaytotitas"),
                EmploymentStartDate = ReadDate(row, "EmploymentStartDate", "DateFrom"),
                EmploymentClassification = Read(row, "EmploymentClassification", "asXaraktirismos"),
                ExperienceYears = Read(row, "ExperienceYears", "Proipiresia"),
                EmploymentRelationship = Read(row, "EmploymentRelationship", "SxesiApasxolisis"),
                EmploymentStatus = Read(row, "EmploymentStatus", "KathestosApasxolisis"),
                WeeklyHours = ReadDouble(row, "WeeklyHours", "WeekHours"),
                WorkScheduleDescription = Read(row, "WorkScheduleDescription", "Orario"),
                BreakDescription = Read(row, "BreakDescription", "Dialeimma"),
                EducationLevel = Read(row, "EducationLevel", "EpipedoMorfosis"),
                DigitalTimeOrganization = Read(row, "DigitalTimeOrganization", "WorkingTimeDigitalOrganization"),
                FullEmploymentHours = ReadDouble(row, "FullEmploymentHours"),
                BreakMinutes = ReadInt(row, "BreakMinutes", "DialeimmaMinutes"),
                BreakWithinWorkingHours = Read(row, "BreakWithinWorkingHours", "DialeimmaEntosWrariou"),
                UsesDigitalWorkCard = Read(row, "UsesDigitalWorkCard", "WorkingCard"),
                FlexibleScheduleMinutes = ReadInt(row, "FlexibleScheduleMinutes", "EueliktoWrario"),
                LastModifiedDate = ReadDate(row, "LastModifiedDate")
            };

            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(item.FirstName)) errors.Add(Tr.Loc["Vm.First name required"]);
            if (string.IsNullOrWhiteSpace(item.LastName)) errors.Add(Tr.Loc["Vm.Last name required"]);
            if (item.TaxId.Length != 9) errors.Add(Tr.Loc["Vm.AFM must be 9 digits"]);
            if (item.SocialSecurityNumber.Length != 11) errors.Add(Tr.Loc["Vm.AMKA must be 11 digits"]);
            if (string.IsNullOrWhiteSpace(item.BarcodeId)) errors.Add(Tr.Loc["Vm.Barcode ID required"]);
            if (item.WeeklyWorkdays is not (5 or 6)) errors.Add(Tr.Loc["Vm.Weekly workdays must be 5 or 6"]);
            if (string.IsNullOrWhiteSpace(item.BranchName)) errors.Add(Tr.Loc["Vm.Branch name required"]);
            if (item.NormalDailyHours <= 0 || item.NormalDailyHours > 24) errors.Add("NormalDailyHours must be greater than 0 and no more than 24.");

            item.IsValid = errors.Count == 0;
            item.Error = errors.Count > 0 ? string.Join("; ", errors) : null;
            result.TotalRows++;
            if (item.IsValid) result.ValidRows.Add(item);
            else result.InvalidRows.Add(item);
        }

        return result;
    }

    // ── WORK CARD HISTORY EXPORT ──────────────────────────────────────────────

    public static string ExportWorkCardHistory(List<WorkCardHistoryDto> records, string folderPath)
    {
        var fileName = $"WorkCardHistory_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        var filePath = Path.Combine(folderPath, fileName);

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(Tr.Loc["Vm.Work Cards"]);

        // Header row
        var headers = new[]
        {
            Tr.Loc["Vm.Employee"], Tr.Loc["Vm.Tax ID (AFM)"], Tr.Loc["Vm.Branch"], Tr.Loc["Vm.Type"],
            Tr.Loc["Vm.Date"], Tr.Loc["Vm.Time"], Tr.Loc["Vm.Submitted"], Tr.Loc["Vm.Protocol"],
            Tr.Loc["Vm.Early Departure"], Tr.Loc["Vm.Early (min)"], Tr.Loc["Vm.Email Alert Sent"], "Automatic"
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
            ws.Cell(row, 7).Value = r.SubmittedToErgani ? Tr.Loc["Vm.Yes"] : Tr.Loc["Vm.No"];
            ws.Cell(row, 8).Value = r.Protocol ?? "";
            ws.Cell(row, 9).Value = r.WasEarlyDeparture ? Tr.Loc["Vm.Yes"] : Tr.Loc["Vm.No"];
            ws.Cell(row, 10).Value = r.EarlyDepartureMinutes?.ToString() ?? "";
            ws.Cell(row, 11).Value = r.EmailAlertSent ? Tr.Loc["Vm.Yes"] : Tr.Loc["Vm.No"];
            ws.Cell(row, 12).Value = r.IsAutomatic ? Tr.Loc["Vm.Yes"] : Tr.Loc["Vm.No"];

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
        ws.Cell(1, 1).Value = string.Format(Tr.Loc["Vm.Schedule: {0}"], employeeFullName);
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Range("A1:G1").Merge();

        var dayHeaders = new[] { Tr.Loc["Vm.Date"], Tr.Loc["Vm.Day"], Tr.Loc["Vm.Work Type"], Tr.Loc["Vm.Start"], Tr.Loc["Vm.End"], Tr.Loc["Vm.Submitted"], Tr.Loc["Vm.Protocol"] };
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
                ws.Cell(row, 6).Value = sched.SubmittedToErgani ? Tr.Loc["Vm.Yes"] : Tr.Loc["Vm.No"];
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

    // ── Ergani declared-schedule import ─────────────────────────────────────

    public sealed class ErganiScheduleImportRow
    {
        public int RowNumber { get; init; }
        public string TaxId { get; init; } = string.Empty;
        public string FirstName { get; init; } = string.Empty;
        public string LastName { get; init; } = string.Empty;
        public int BranchNumber { get; init; }
        public DateOnly Date { get; init; }
        public string Employment { get; init; } = string.Empty;
        public AppWorkType WorkType { get; init; }
        public TimeOnly? StartTime { get; init; }
        public TimeOnly? EndTime { get; init; }
        /// <summary>Every continuous "ΕΡΓΑΣΙΑ hh:mm-hh:mm" interval in the downloaded Ergani grid row.</summary>
        public IReadOnlyList<(TimeOnly Start, TimeOnly End)> WorkingPeriods { get; init; }
            = Array.Empty<(TimeOnly, TimeOnly)>();
        public bool HasOvertime => OvertimeRanges.Count > 0;
        public bool HasLeave { get; init; }
        public string? LeaveDescription { get; init; }

        /// <summary>The "Υπερωρία hh:mm-hh:mm" parts of the declaration. They are kept apart from
        /// <see cref="StartTime"/>/<see cref="EndTime"/> (the "ΕΡΓΑΣΙΑ" part) so work and
        /// overtime are imported as two separate things, never as one block.</summary>
        public IReadOnlyList<(TimeOnly Start, TimeOnly End)> OvertimeRanges { get; init; }
            = Array.Empty<(TimeOnly, TimeOnly)>();

        /// <summary>Ergani leave code resolved from the description (e.g. "Κανονική άδεια" → ΑΔΚΑΝ).</summary>
        public string? LeaveTypeCode { get; init; }
        public int? LeaveReferenceYear { get; init; }

        /// <summary>A whole-day leave: a holiday, NOT an absence and NOT a work day.</summary>
        public bool IsLeaveOnly => HasLeave && !(StartTime.HasValue && EndTime.HasValue);
    }

    public sealed class ErganiScheduleImportResult
    {
        public List<ErganiScheduleImportRow> Rows { get; } = new();
        public List<string> Warnings { get; } = new();
        public int TotalRows { get; set; }
    }

    /// <summary>
    /// Parses the schedule export downloaded from the Ergani portal.
    /// Expected columns are identified by their Greek header names rather than
    /// fixed positions so minor column reordering does not break the import.
    /// </summary>
    public static ErganiScheduleImportResult ParseErganiScheduleExport(string filePath)
    {
        using var wb = new XLWorkbook(filePath);
        var ws = wb.Worksheets.First();
        var result = new ErganiScheduleImportResult();

        var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var headerRow = ws.FirstRowUsed()?.RowNumber() ?? 1;
        foreach (var cell in ws.Row(headerRow).CellsUsed())
        {
            var h = NormalizeHeader(cell.GetString());
            if (!string.IsNullOrWhiteSpace(h))
                headerMap[h] = cell.Address.ColumnNumber;
        }

        int Col(params string[] names)
        {
            foreach (var name in names)
                if (headerMap.TryGetValue(NormalizeHeader(name), out var c)) return c;
            return 0;
        }

        var branchCol = Col("ΑΑ Παραρτήματος", "ΑΑ Παραρτήματος");
        var afmCol = Col("ΑΦΜ", "AFM", "Tax ID");
        var firstCol = Col("Όνομα", "Ονομα", "First Name");
        var lastCol = Col("Επώνυμο", "Επωνυμο", "Last Name");
        var dateCol = Col("Ημ/νια", "Ημ/νία", "Ημερομηνία", "Date");
        var employmentCol = Col("Απασχόληση", "Απασχοληση", "Employment");

        if (afmCol == 0 || dateCol == 0 || employmentCol == 0)
            throw new InvalidDataException(
                Tr.Loc["Vm.This does not appear to be an Ergani declared schedule export. Required columns: ΑΦΜ, Ημ/νια and Απασχόληση."]);

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow;
        for (int r = headerRow + 1; r <= lastRow; r++)
        {
            if (ws.Row(r).CellsUsed().All(c => string.IsNullOrWhiteSpace(c.GetString())))
                continue;

            result.TotalRows++;

            var afm = DigitsOnly(ws.Cell(r, afmCol).GetString());
            if (afm.Length > 0 && afm.Length < 9) afm = afm.PadLeft(9, '0');
            var employment = ws.Cell(r, employmentCol).GetString().Trim();
            if (string.IsNullOrWhiteSpace(afm) || string.IsNullOrWhiteSpace(employment))
            {
                result.Warnings.Add(string.Format(Tr.Loc["Vm.Excel row {0}: missing AFM or employment description."], r));
                continue;
            }

            if (!TryReadDate(ws.Cell(r, dateCol), out var date))
            {
                result.Warnings.Add(string.Format(Tr.Loc["Vm.Excel row {0}: invalid date."], r));
                continue;
            }

            // Keyword matching ignores accents and case ("Υπερωρία" = "ΥΠΕΡΩΡΙΑ").
            var folded = NormalizeHeader(employment);

            var workingPeriods = ParseWorkRanges(folded);
            var start = workingPeriods.Count > 0 ? workingPeriods[0].Start : (TimeOnly?)null;
            var end = workingPeriods.Count > 0 ? workingPeriods[^1].End : (TimeOnly?)null;
            var overtimeRanges = ParseOvertimeRanges(folded);
            if (overtimeRanges.Count == 0 && Regex.IsMatch(folded, @"ΥΠΕΡΩΡΙΑ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                result.Warnings.Add(string.Format(Tr.Loc["Vm.Excel row {0}: overtime mentioned but no hh:mm-hh:mm range found ({1})."], r, employment));

            var hasLeave = Regex.IsMatch(folded, @"\bΑΔΕΙΑ\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            string? leaveCode = null;
            int? leaveYear = null;
            if (hasLeave)
                (leaveCode, leaveYear) = ResolveLeaveType(folded);

            if (hasLeave && start.HasValue && end.HasValue)
                result.Warnings.Add(string.Format(Tr.Loc["Vm.Excel row {0}: work time and leave on the same day — only the work time is imported ({1})."], r, employment));

            // A day without a work range is a rest day. Whole-day leave is handled separately
            // (IsLeaveOnly): it becomes a holiday record, it is never stored as "Absent".
            var workType = start.HasValue && end.HasValue
                ? AppWorkType.Office
                : AppWorkType.Rest;

            int branchNumber = 0;
            if (branchCol != 0)
                int.TryParse(ws.Cell(r, branchCol).GetString().Trim(), out branchNumber);

            result.Rows.Add(new ErganiScheduleImportRow
            {
                RowNumber = r,
                TaxId = afm,
                FirstName = firstCol == 0 ? string.Empty : ws.Cell(r, firstCol).GetString().Trim(),
                LastName = lastCol == 0 ? string.Empty : ws.Cell(r, lastCol).GetString().Trim(),
                BranchNumber = branchNumber,
                Date = date,
                Employment = employment,
                WorkType = workType,
                StartTime = start,
                EndTime = end,
                WorkingPeriods = workingPeriods,
                OvertimeRanges = overtimeRanges,
                HasLeave = hasLeave,
                LeaveDescription = hasLeave ? employment : null,
                LeaveTypeCode = leaveCode,
                LeaveReferenceYear = leaveYear
            });
        }

        return result;
    }

    private static string NormalizeHeader(string value)
    {
        var normalized = value.Trim().Replace("\u00A0", " ").Normalize(NormalizationForm.FormD);
        return new string(normalized
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray())
            .Normalize(NormalizationForm.FormC);
    }

    private static string DigitsOnly(string value) =>
        new(value.Where(char.IsDigit).ToArray());

    private static bool TryReadDate(IXLCell cell, out DateOnly date)
    {
        if (cell.DataType == XLDataType.DateTime)
        {
            date = DateOnly.FromDateTime(cell.GetDateTime());
            return true;
        }

        var text = cell.GetString().Trim();
        if (DateOnly.TryParse(text, new CultureInfo("el-GR"), DateTimeStyles.None, out date))
            return true;
        if (DateTime.TryParse(text, new CultureInfo("el-GR"), DateTimeStyles.None, out var dt))
        {
            date = DateOnly.FromDateTime(dt);
            return true;
        }
        date = default;
        return false;
    }

    /// <summary>All "Υπερωρία hh:mm-hh:mm" ranges of a declaration (text already accent-folded).</summary>
    private static List<(TimeOnly Start, TimeOnly End)> ParseOvertimeRanges(string foldedText)
    {
        var list = new List<(TimeOnly, TimeOnly)>();
        foreach (Match m in Regex.Matches(foldedText,
                     @"ΥΠΕΡΩΡΙΑΣ?\D*?(?<start>\d{1,2}:\d{2})\s*-\s*(?<end>\d{1,2}:\d{2})",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            if (TimeOnly.TryParse(m.Groups["start"].Value, out var s) &&
                TimeOnly.TryParse(m.Groups["end"].Value, out var e) && e > s)
                list.Add((s, e));
        }
        return list;
    }

    /// <summary>Maps the leave text to an Ergani leave code and reads "Έτος 2026" when present.</summary>
    private static (string Code, int? Year) ResolveLeaveType(string foldedText)
    {
        int? year = null;
        var ym = Regex.Match(foldedText, @"ΕΤΟΣ\s*[:\-]?\s*(?<y>\d{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (ym.Success && int.TryParse(ym.Groups["y"].Value, out var y)) year = y;

        // Longest description first so "Άδεια ... (πιο ειδική)" wins over a shorter prefix.
        foreach (var type in ErganiManager.Core.Models.LeaveTypes.All
                     .Where(t => !t.IsHourly)
                     .OrderByDescending(t => t.Description.Length))
        {
            if (foldedText.Contains(NormalizeHeader(type.Description), StringComparison.OrdinalIgnoreCase))
                return (type.Code, year);
        }

        return ("ΑΔΑΛ", year); // "Άδεια Άλλη" — an unrecognised leave text
    }

    private static List<(TimeOnly Start, TimeOnly End)> ParseWorkRanges(string text)
    {
        var periods = new List<(TimeOnly Start, TimeOnly End)>();
        foreach (Match m in Regex.Matches(text,
                     @"\b(?:ΕΡΓΑΣΙΑ|ΕΡΓΑΣΙΑΣ)\s+(?<start>\d{1,2}:\d{2})\s*-\s*(?<end>\d{1,2}:\d{2})",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            if (TimeOnly.TryParse(m.Groups["start"].Value, out var start) &&
                TimeOnly.TryParse(m.Groups["end"].Value, out var end) &&
                end > start)
                periods.Add((start, end));
        }

        // Keep intervals in the order shown in the Ergani export. Warn rather than silently
        // merge intervals; the import workflow can preserve each separate work period.
        return periods;
    }

    // Backwards-compatible parser for the application's own simple export format.
    public static List<ScheduleImportRow> ParseScheduleImportFile(string filePath)
    {
        using var wb = new XLWorkbook(filePath);
        var ws = wb.Worksheets.First();
        var rows = new List<ScheduleImportRow>();
        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

        for (int r = 2; r <= lastRow; r++)
        {
            if (ws.Cell(r, 1).IsEmpty()) continue;
            if (!TryReadDate(ws.Cell(r, 1), out var date)) continue;

            var workTypeStr = ws.Cell(r, 2).GetValue<string>().Trim().ToLowerInvariant();
            var workType = workTypeStr switch
            {
                "office" or "work" => (AppWorkType?)AppWorkType.Office,
                "home" => AppWorkType.Home,
                "rest" => AppWorkType.Rest,
                "absent" => AppWorkType.Absent,
                _ => null
            };
            if (workType == null) continue;

            TimeOnly? start = null, end = null;
            if (TimeOnly.TryParse(ws.Cell(r, 3).GetValue<string>(), out var st)) start = st;
            if (TimeOnly.TryParse(ws.Cell(r, 4).GetValue<string>(), out var et)) end = et;

            var comments = ws.Cell(r, 5).GetValue<string>().Trim();
            rows.Add(new ScheduleImportRow
            {
                Date = date,
                WorkType = workType.Value,
                StartTime = start,
                EndTime = end,
                Comments = string.IsNullOrEmpty(comments) ? null : comments
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
        var headers = new[]
        {
            "FirstName", "LastName", "TaxId", "SocialSecurityNumber", "BarcodeId",
            "ProfessionCode", "WeeklyWorkdays", "BranchName", "IsActive", "NormalDailyHours",
            "FatherName", "MotherName", "BirthDate", "Sex", "Nationality", "MaritalStatus",
            "NumberOfChildren", "IdentityDocumentType", "IdentityDocumentNumber", "EmploymentStartDate",
            "EmploymentClassification", "ExperienceYears", "EmploymentRelationship", "EmploymentStatus",
            "WeeklyHours", "WorkScheduleDescription", "BreakDescription", "EducationLevel",
            "DigitalTimeOrganization", "FullEmploymentHours", "BreakMinutes",
            "BreakWithinWorkingHours", "UsesDigitalWorkCard", "FlexibleScheduleMinutes", "LastModifiedDate"
        };

        for (int col = 0; col < headers.Length; col++)
        {
            var cell = ws.Cell(1, col + 1);
            cell.Value = headers[col];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0x1E, 0x21, 0x28);
            cell.Style.Font.FontColor = XLColor.White;
        }

        int row = 2;
        foreach (var e in employees)
        {
            branchNames.TryGetValue(e.BranchId, out var branchName);
            var values = new object?[]
            {
                e.FirstName, e.LastName, e.TaxId, e.SocialSecurityNumber, e.BarcodeId,
                e.ProfessionCode, e.WeeklyWorkdays, branchName ?? string.Empty, e.IsActive, e.NormalDailyHours,
                e.FatherName, e.MotherName, e.BirthDate?.ToString("O", CultureInfo.InvariantCulture),
                e.Sex, e.Nationality, e.MaritalStatus, e.NumberOfChildren, e.IdentityDocumentType,
                e.IdentityDocumentNumber, e.EmploymentStartDate?.ToString("O", CultureInfo.InvariantCulture),
                e.EmploymentClassification, e.ExperienceYears, e.EmploymentRelationship, e.EmploymentStatus,
                e.WeeklyHours, e.WorkScheduleDescription, e.BreakDescription, e.EducationLevel,
                e.DigitalTimeOrganization, e.FullEmploymentHours, e.BreakMinutes,
                e.BreakWithinWorkingHours, e.UsesDigitalWorkCard, e.FlexibleScheduleMinutes,
                e.LastModifiedDate?.ToString("O", CultureInfo.InvariantCulture)
            };

            for (int col = 0; col < values.Length; col++)
                ws.Cell(row, col + 1).Value = XLCellValue.FromObject(values[col]);

            if (!e.IsActive)
                ws.Range(row, 1, row, headers.Length).Style.Font.FontColor = XLColor.Gray;
            row++;
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);
        ws.RangeUsed()?.SetAutoFilter();

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
