using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.Core.Services;

public class CompanyService : ICompanyService
{
    private readonly IConnectionStateService _connectionState;
    private readonly ICredentialProtector _credentialProtector;

    public CompanyService(IConnectionStateService connectionState, ICredentialProtector credentialProtector)
    {
        _connectionState = connectionState;
        _credentialProtector = credentialProtector;
    }

    private AppDbContext OpenDb()
        => new AppDbContext(_connectionState.GetDbOptions());

    public async Task<List<CompanyDto>> GetAllAsync()
    {
        await using var db = OpenDb();
        var companies = await db.Companies.OrderBy(c => c.Name).ToListAsync();
        return companies.Select(ToDto).ToList();
    }

    public async Task<CompanyDto?> GetByIdAsync(int id)
    {
        await using var db = OpenDb();
        var company = await db.Companies.FindAsync(id);
        return company == null ? null : ToDto(company);
    }

    public async Task<int> CreateAsync(CompanyDto dto)
    {
        await using var db = OpenDb();

        var entity = new Company
        {
            Name = dto.Name.Trim(),
            TaxId = dto.TaxId.Trim(),
            ErganiUsername = dto.ErganiUsername.Trim(),
            ErganiPasswordEncrypted = _credentialProtector.Protect(dto.ErganiPasswordPlainText ?? string.Empty),
            ErganiBaseUrl = dto.ErganiBaseUrl,
            IsActive = dto.IsActive,
            EarlyClockInBlockMinutes = dto.EarlyClockInBlockMinutes,
            EarlyDepartureAlertMinutes = dto.EarlyDepartureAlertMinutes,
            BlockClockInWithoutSchedule = dto.BlockClockInWithoutSchedule,
            AlertEmailEnabled = dto.AlertEmailEnabled,
            AutoRetryFailedSubmissions = dto.AutoRetryFailedSubmissions,
            AlertEmailRecipients = dto.AlertEmailRecipients,
            SmtpHost = dto.SmtpHost,
            SmtpPort = dto.SmtpPort,
            SmtpUser = dto.SmtpUser,
            SmtpPasswordEncrypted = string.IsNullOrEmpty(dto.SmtpPasswordPlainText)
                ? null
                : _credentialProtector.Protect(dto.SmtpPasswordPlainText),
            SmtpUseTls = dto.SmtpUseTls,
            CreatedAt = DateTime.UtcNow
        };

        db.Companies.Add(entity);
        await db.SaveChangesAsync();
        return entity.Id;
    }

    public async Task UpdateAsync(CompanyDto dto)
    {
        await using var db = OpenDb();

        var entity = await db.Companies.FindAsync(dto.Id)
            ?? throw new InvalidOperationException($"Company {dto.Id} not found.");

        entity.Name = dto.Name.Trim();
        entity.TaxId = dto.TaxId.Trim();
        entity.ErganiUsername = dto.ErganiUsername.Trim();
        entity.ErganiBaseUrl = dto.ErganiBaseUrl;
        entity.IsActive = dto.IsActive;
        entity.EarlyClockInBlockMinutes = dto.EarlyClockInBlockMinutes;
        entity.EarlyDepartureAlertMinutes = dto.EarlyDepartureAlertMinutes;
        entity.BlockClockInWithoutSchedule = dto.BlockClockInWithoutSchedule;
        entity.AlertEmailEnabled = dto.AlertEmailEnabled;
        entity.AutoRetryFailedSubmissions = dto.AutoRetryFailedSubmissions;
        entity.AlertEmailRecipients = dto.AlertEmailRecipients;
        entity.SmtpHost = dto.SmtpHost;
        entity.SmtpPort = dto.SmtpPort;
        entity.SmtpUser = dto.SmtpUser;
        entity.SmtpUseTls = dto.SmtpUseTls;

        // Only re-encrypt and overwrite the password if the caller actually
        // supplied a new one — the DTO's plaintext field is left blank on
        // reads, so an UpdateAsync call after a GetByIdAsync round-trip
        // without editing the password field must NOT wipe the stored value.
        if (!string.IsNullOrEmpty(dto.ErganiPasswordPlainText))
            entity.ErganiPasswordEncrypted = _credentialProtector.Protect(dto.ErganiPasswordPlainText);

        if (!string.IsNullOrEmpty(dto.SmtpPasswordPlainText))
            entity.SmtpPasswordEncrypted = _credentialProtector.Protect(dto.SmtpPasswordPlainText);

        await db.SaveChangesAsync();
    }

    public async Task SetActiveAsync(int id, bool isActive)
    {
        await using var db = OpenDb();
        var entity = await db.Companies.FindAsync(id)
            ?? throw new InvalidOperationException($"Company {id} not found.");
        entity.IsActive = isActive;
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = OpenDb();
        var entity = await db.Companies.FindAsync(id);
        if (entity != null)
        {
            db.Companies.Remove(entity);
            await db.SaveChangesAsync();
        }
    }

    private static CompanyDto ToDto(Company c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        TaxId = c.TaxId,
        ErganiUsername = c.ErganiUsername,
        ErganiPasswordPlainText = null, // never expose the encrypted/decrypted password on read
        ErganiBaseUrl = c.ErganiBaseUrl,
        IsActive = c.IsActive,
        EarlyClockInBlockMinutes = c.EarlyClockInBlockMinutes,
        EarlyDepartureAlertMinutes = c.EarlyDepartureAlertMinutes,
        BlockClockInWithoutSchedule = c.BlockClockInWithoutSchedule,
        AlertEmailEnabled = c.AlertEmailEnabled,
        AutoRetryFailedSubmissions = c.AutoRetryFailedSubmissions,
        AlertEmailRecipients = c.AlertEmailRecipients,
        SmtpHost = c.SmtpHost,
        SmtpPort = c.SmtpPort,
        SmtpUser = c.SmtpUser,
        SmtpPasswordPlainText = null,
        SmtpUseTls = c.SmtpUseTls
    };
}
