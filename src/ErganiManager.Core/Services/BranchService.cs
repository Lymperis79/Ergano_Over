using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.Core.Services;

public class BranchService : IBranchService
{
    private readonly IConnectionStateService _connectionState;

    public BranchService(IConnectionStateService connectionState)
    {
        _connectionState = connectionState;
    }

    private AppDbContext OpenDb()
        => new AppDbContext(_connectionState.GetDbOptions());

    public async Task<List<BranchDto>> GetByCompanyAsync(int companyId)
    {
        await using var db = OpenDb();
        var branches = await db.Branches
            .Where(b => b.CompanyId == companyId)
            .OrderBy(b => b.BranchNumber)
            .ToListAsync();
        return branches.Select(ToDto).ToList();
    }

    public async Task<BranchDto?> GetByIdAsync(int id)
    {
        await using var db = OpenDb();
        var branch = await db.Branches.FindAsync(id);
        return branch == null ? null : ToDto(branch);
    }

    public async Task<int> CreateAsync(BranchDto dto)
    {
        await using var db = OpenDb();

        var duplicateNumber = await db.Branches
            .AnyAsync(b => b.CompanyId == dto.CompanyId && b.BranchNumber == dto.BranchNumber);
        if (duplicateNumber)
            throw new InvalidOperationException($"Branch number {dto.BranchNumber} already exists for this company.");

        var entity = new BusinessBranch
        {
            CompanyId = dto.CompanyId,
            BranchNumber = dto.BranchNumber,
            Address = dto.Address.Trim(),
            SepeServiceCode = dto.SepeServiceCode.Trim(),
            OaedServiceCode = dto.OaedServiceCode?.Trim(),
            ActivityCode = dto.ActivityCode.Trim(),
            KallikratisMunicipalCode = dto.KallikratisMunicipalCode.Trim(),
            Name = dto.Name?.Trim(),
            IsActive = dto.IsActive
        };

        db.Branches.Add(entity);
        await db.SaveChangesAsync();
        return entity.Id;
    }

    public async Task UpdateAsync(BranchDto dto)
    {
        await using var db = OpenDb();

        var entity = await db.Branches.FindAsync(dto.Id)
            ?? throw new InvalidOperationException($"Branch {dto.Id} not found.");

        if (entity.BranchNumber != dto.BranchNumber)
        {
            var duplicateNumber = await db.Branches
                .AnyAsync(b => b.CompanyId == dto.CompanyId && b.BranchNumber == dto.BranchNumber && b.Id != dto.Id);
            if (duplicateNumber)
                throw new InvalidOperationException($"Branch number {dto.BranchNumber} already exists for this company.");
        }

        entity.BranchNumber = dto.BranchNumber;
        entity.Address = dto.Address.Trim();
        entity.SepeServiceCode = dto.SepeServiceCode.Trim();
        entity.OaedServiceCode = dto.OaedServiceCode?.Trim();
        entity.ActivityCode = dto.ActivityCode.Trim();
        entity.KallikratisMunicipalCode = dto.KallikratisMunicipalCode.Trim();
        entity.Name = dto.Name?.Trim();
        entity.IsActive = dto.IsActive;

        await db.SaveChangesAsync();
    }

    public async Task SetActiveAsync(int id, bool isActive)
    {
        await using var db = OpenDb();
        var entity = await db.Branches.FindAsync(id)
            ?? throw new InvalidOperationException($"Branch {id} not found.");
        entity.IsActive = isActive;
        await db.SaveChangesAsync();
    }

    private static BranchDto ToDto(BusinessBranch b) => new()
    {
        Id = b.Id,
        CompanyId = b.CompanyId,
        BranchNumber = b.BranchNumber,
        Address = b.Address,
        SepeServiceCode = b.SepeServiceCode,
        OaedServiceCode = b.OaedServiceCode,
        ActivityCode = b.ActivityCode,
        KallikratisMunicipalCode = b.KallikratisMunicipalCode,
        Name = b.Name,
        IsActive = b.IsActive
    };

    public async Task DeleteAsync(int id)
    {
        await using var db = OpenDb();
        var entity = await db.Branches.FindAsync(id);
        if (entity != null)
        {
            db.Branches.Remove(entity);
            await db.SaveChangesAsync();
        }
    }
}