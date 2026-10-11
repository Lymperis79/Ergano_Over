using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.Core.Services;

public class ImportLogService : IImportLogService
{
    private readonly IConnectionStateService _connectionState;

    public ImportLogService(IConnectionStateService connectionState)
    {
        _connectionState = connectionState;
    }

    private AppDbContext OpenDb() => new(_connectionState.GetDbOptions());

    /// <summary>
    /// Runs a database action; if it fails (for example because this table was added by a newer
    /// version and the database was not upgraded yet) the schema is brought up to date once and the
    /// action is repeated. No data is touched by the upgrade.
    /// </summary>
    private async Task<T> WithSchemaRepairAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception) when (_connectionState.LoadConfig() != null)
        {
            DbSchemaUpdater.ResetCheck();
            await DbSchemaUpdater.EnsureUpToDateAsync(_connectionState.LoadConfig()!);
            return await action();
        }
    }

    public Task AddAsync(ImportLogDto entry, int companyId)
        => WithSchemaRepairAsync(async () => { await AddCoreAsync(entry, companyId); return true; });

    public Task<List<ImportLogDto>> GetRecentAsync(int companyId, int take = 50)
        => WithSchemaRepairAsync(() => GetRecentCoreAsync(companyId, take));

    private async Task AddCoreAsync(ImportLogDto entry, int companyId)
    {
        await using var db = OpenDb();
        db.ErganiImportLogs.Add(new ErganiImportLog
        {
            CompanyId       = companyId,
            ImportedAt      = DateTime.UtcNow,
            ImportedBy      = Trim(entry.ImportedBy, 100),
            FileName        = Trim(entry.FileName, 260),
            Origin          = Trim(entry.Origin, 20),
            FromDate        = entry.FromDate,
            ToDate          = entry.ToDate,
            EmployeeCount   = entry.EmployeeCount,
            ScheduleDays    = entry.ScheduleDays,
            OvertimeRecords = entry.OvertimeRecords,
            HolidayDays     = entry.HolidayDays,
            SkippedRows     = entry.SkippedRows
        });
        await db.SaveChangesAsync();
    }

    private async Task<List<ImportLogDto>> GetRecentCoreAsync(int companyId, int take)
    {
        await using var db = OpenDb();
        var list = await db.ErganiImportLogs
            .Where(l => l.CompanyId == companyId)
            .OrderByDescending(l => l.ImportedAt)
            .Take(take)
            .ToListAsync();

        return list.Select(l => new ImportLogDto
        {
            Id              = l.Id,
            ImportedAt      = DateTime.SpecifyKind(l.ImportedAt, DateTimeKind.Utc),
            ImportedBy      = l.ImportedBy,
            FileName        = l.FileName,
            Origin          = l.Origin,
            FromDate        = l.FromDate,
            ToDate          = l.ToDate,
            EmployeeCount   = l.EmployeeCount,
            ScheduleDays    = l.ScheduleDays,
            OvertimeRecords = l.OvertimeRecords,
            HolidayDays     = l.HolidayDays,
            SkippedRows     = l.SkippedRows
        }).ToList();
    }

    private static string Trim(string? value, int max)
        => string.IsNullOrEmpty(value) ? string.Empty : (value.Length > max ? value[..max] : value);
}
