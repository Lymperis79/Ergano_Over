using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.Core.Services;

/// <summary>Database-backed leave-type catalog with a local JSON fallback for offline use.</summary>
public sealed class LeaveTypeService : ILeaveTypeService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly object Gate = new();
    private static string ConfigPath => Path.Combine(
        ErganiManager.LocalCache.AppPaths.GetAppDataFolder(), "leave-types.json");
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private IReadOnlyList<LeaveTypeInfo>? _configured;
    private DateTime _lastRefreshUtc = DateTime.MinValue;

    public LeaveTypeService(IDbContextFactory<AppDbContext> dbFactory) => _dbFactory = dbFactory;

    public IReadOnlyList<LeaveTypeInfo> All
    {
        get
        {
            lock (Gate)
            {
                if (_configured == null || DateTime.UtcNow - _lastRefreshUtc >= RefreshInterval)
                    RefreshFromDatabase();
                return _configured ??= ReadLocalCacheOrDefaults();
            }
        }
    }

    public void Refresh()
    {
        lock (Gate)
        {
            _lastRefreshUtc = DateTime.MinValue;
            RefreshFromDatabase();
        }
    }

    private void RefreshFromDatabase()
    {
        try
        {
            using var db = _dbFactory.CreateDbContext();
            var rows = db.LeaveTypeConfigurations.AsNoTracking().OrderBy(t => t.Code).ToList();
            if (rows.Count == 0)
            {
                var seed = ReadLocalCacheOrDefaults();
                db.LeaveTypeConfigurations.AddRange(seed.Select(ToEntity));
                db.SaveChanges();
                rows = db.LeaveTypeConfigurations.AsNoTracking().OrderBy(t => t.Code).ToList();
            }

            var newOfficialTypes = LeaveTypes.CreateDefaults().Where(t =>
                t.Code is "ΩΑΜΓΤΑ" or "ΑΔΠΓ" or "ΑΔΓΕ" or "ΑΔΕΚΛ").ToList();
            var existingCodes = rows.Select(t => t.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = newOfficialTypes.Where(t => !existingCodes.Contains(t.Code)).ToList();
            if (missing.Count > 0)
            {
                db.LeaveTypeConfigurations.AddRange(missing.Select(ToEntity));
                db.SaveChanges();
                rows = db.LeaveTypeConfigurations.AsNoTracking().OrderBy(t => t.Code).ToList();
            }

            _configured = rows.Select(t => new LeaveTypeInfo(t.Code, t.Description, t.IsHourly, t.IsPaid,
                string.IsNullOrWhiteSpace(t.PaymentCategory)
                    ? LeaveTypes.DefaultPaymentCategory(t.Code, t.IsPaid) : t.PaymentCategory)).ToList();
            _lastRefreshUtc = DateTime.UtcNow;
            try { WriteLocalCache(_configured); } catch { /* central database remains authoritative */ }
        }
        catch
        {
            _lastRefreshUtc = DateTime.UtcNow;
            _configured ??= ReadLocalCacheOrDefaults();
        }
    }

    public void Save(IEnumerable<LeaveTypeInfo> types)
    {
        var list = types.ToList();
        if (list.Count == 0)
            throw new InvalidOperationException(UiText.T("At least one leave type is required."));
        if (list.Any(t => string.IsNullOrWhiteSpace(t.Code) || t.Code.Trim().Length > 10 ||
                          string.IsNullOrWhiteSpace(t.Description) || t.Description.Trim().Length > 200))
            throw new InvalidOperationException(UiText.T("Every leave type needs a code (up to 10 characters) and a description (up to 200 characters)."));
        if (list.GroupBy(t => t.Code.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new InvalidOperationException(UiText.T("Leave type codes must be unique."));

        list = list.Select(t => t with { Code = t.Code.Trim(), Description = t.Description.Trim() }).ToList();
        lock (Gate)
        {
            using var db = _dbFactory.CreateDbContext();
            using var transaction = db.Database.BeginTransaction();
            db.Database.ExecuteSqlRaw("DELETE FROM LeaveTypeConfigurations");
            db.LeaveTypeConfigurations.AddRange(list.Select(ToEntity));
            db.SaveChanges();
            transaction.Commit();

            _configured = list.Select(t => t with
            {
                PaymentCategory = t.EffectivePaymentCategory,
                IsPaid = t.EffectivePaymentCategory != "Unpaid"
            }).ToList();
            _lastRefreshUtc = DateTime.UtcNow;
            try { WriteLocalCache(_configured); } catch { /* local cache is optional */ }
        }
    }

    public LeaveTypeInfo? Find(string? code) => All.FirstOrDefault(t =>
        string.Equals(t.Code, code, StringComparison.OrdinalIgnoreCase));

    public string Label(string? code) => Find(code)?.Label ?? code ?? string.Empty;

    private static LeaveTypeConfiguration ToEntity(LeaveTypeInfo type) => new()
    {
        Code = type.Code,
        Description = type.Description,
        IsHourly = type.IsHourly,
        IsPaid = type.EffectivePaymentCategory != "Unpaid",
        PaymentCategory = type.EffectivePaymentCategory,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static IReadOnlyList<LeaveTypeInfo> ReadLocalCacheOrDefaults()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var loaded = System.Text.Json.JsonSerializer.Deserialize<List<LeaveTypeInfo>>(File.ReadAllText(ConfigPath));
                if (loaded is { Count: > 0 }) return loaded;
            }
        }
        catch { /* use built-in defaults if the cache is unreadable */ }
        return LeaveTypes.CreateDefaults();
    }

    private static void WriteLocalCache(IEnumerable<LeaveTypeInfo> types)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, System.Text.Json.JsonSerializer.Serialize(types,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
