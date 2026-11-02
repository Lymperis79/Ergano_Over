using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.Core.Services;

public class EmployeeService : IEmployeeService
{
    private readonly IConnectionStateService _connectionState;

    public EmployeeService(IConnectionStateService connectionState)
    {
        _connectionState = connectionState;
    }

    private AppDbContext OpenDb()
        => new AppDbContext(_connectionState.GetDbOptions());

    public async Task<List<EmployeeDto>> GetByCompanyAsync(int companyId, bool activeOnly = false)
    {
        await using var db = OpenDb();
        var query = db.Employees.Where(e => e.CompanyId == companyId);
        if (activeOnly)
            query = query.Where(e => e.IsActive);

        var employees = await query.OrderBy(e => e.LastName).ThenBy(e => e.FirstName).ToListAsync();
        return employees.Select(ToDto).ToList();
    }

    public async Task<EmployeeDto?> GetByIdAsync(int id)
    {
        await using var db = OpenDb();
        var employee = await db.Employees.FindAsync(id);
        return employee == null ? null : ToDto(employee);
    }

    public async Task<bool> IsBarcodeTakenAsync(int companyId, string barcodeId, int? excludeEmployeeId = null)
    {
        await using var db = OpenDb();
        var query = db.Employees.Where(e => e.CompanyId == companyId && e.BarcodeId == barcodeId);
        if (excludeEmployeeId.HasValue)
            query = query.Where(e => e.Id != excludeEmployeeId.Value);
        return await query.AnyAsync();
    }

    public async Task<int> CreateAsync(EmployeeDto dto)
    {
        ValidateBasics(dto);

        await using var db = OpenDb();

        var barcodeTaken = await db.Employees
            .AnyAsync(e => e.CompanyId == dto.CompanyId && e.BarcodeId == dto.BarcodeId);
        if (barcodeTaken)
            throw new InvalidOperationException(ErganiManager.Data.UiText.F("Barcode '{0}' is already assigned to another employee in this company.", dto.BarcodeId));

        var entity = new Employee
        {
            CompanyId = dto.CompanyId,
            BranchId = dto.BranchId,
            FirstName = dto.FirstName.Trim(),
            LastName = dto.LastName.Trim(),
            TaxId = dto.TaxId.Trim(),
            SocialSecurityNumber = dto.SocialSecurityNumber.Trim(),
            BarcodeId = dto.BarcodeId.Trim(),
            ProfessionCode = dto.ProfessionCode.Trim(),
            WeeklyWorkdays = dto.WeeklyWorkdays,
            FatherName = dto.FatherName,
            MotherName = dto.MotherName,
            BirthDate = dto.BirthDate,
            Sex = dto.Sex,
            Nationality = dto.Nationality,
            MaritalStatus = dto.MaritalStatus,
            NumberOfChildren = dto.NumberOfChildren,
            IdentityDocumentType = dto.IdentityDocumentType,
            IdentityDocumentNumber = dto.IdentityDocumentNumber,
            EmploymentStartDate = dto.EmploymentStartDate,
            EmploymentClassification = dto.EmploymentClassification,
            ExperienceYears = dto.ExperienceYears,
            EmploymentRelationship = dto.EmploymentRelationship,
            EmploymentStatus = dto.EmploymentStatus,
            WeeklyHours = dto.WeeklyHours,
            WorkScheduleDescription = dto.WorkScheduleDescription,
            BreakDescription = dto.BreakDescription,
            EducationLevel = dto.EducationLevel,
            DigitalTimeOrganization = dto.DigitalTimeOrganization,
            FullEmploymentHours = dto.FullEmploymentHours,
            BreakMinutes = dto.BreakMinutes,
            BreakWithinWorkingHours = dto.BreakWithinWorkingHours,
            UsesDigitalWorkCard = dto.UsesDigitalWorkCard,
            FlexibleScheduleMinutes = dto.FlexibleScheduleMinutes,
            LastModifiedDate = dto.LastModifiedDate,
            NormalDailyHours = dto.NormalDailyHours,
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        db.Employees.Add(entity);
        await db.SaveChangesAsync();
        return entity.Id;
    }

    public async Task UpdateAsync(EmployeeDto dto)
    {
        ValidateBasics(dto);

        await using var db = OpenDb();

        var entity = await db.Employees.FindAsync(dto.Id)
            ?? throw new InvalidOperationException(ErganiManager.Data.UiText.F("Employee {0} not found.", dto.Id));

        if (entity.BarcodeId != dto.BarcodeId)
        {
            var barcodeTaken = await db.Employees
                .AnyAsync(e => e.CompanyId == dto.CompanyId && e.BarcodeId == dto.BarcodeId && e.Id != dto.Id);
            if (barcodeTaken)
                throw new InvalidOperationException(ErganiManager.Data.UiText.F("Barcode '{0}' is already assigned to another employee in this company.", dto.BarcodeId));
        }

        entity.BranchId = dto.BranchId;
        entity.FirstName = dto.FirstName.Trim();
        entity.LastName = dto.LastName.Trim();
        entity.TaxId = dto.TaxId.Trim();
        entity.SocialSecurityNumber = dto.SocialSecurityNumber.Trim();
        entity.BarcodeId = dto.BarcodeId.Trim();
        entity.ProfessionCode = dto.ProfessionCode.Trim();
        entity.WeeklyWorkdays = dto.WeeklyWorkdays;
        entity.FatherName = dto.FatherName;
        entity.MotherName = dto.MotherName;
        entity.BirthDate = dto.BirthDate;
        entity.Sex = dto.Sex;
        entity.Nationality = dto.Nationality;
        entity.MaritalStatus = dto.MaritalStatus;
        entity.NumberOfChildren = dto.NumberOfChildren;
        entity.IdentityDocumentType = dto.IdentityDocumentType;
        entity.IdentityDocumentNumber = dto.IdentityDocumentNumber;
        entity.EmploymentStartDate = dto.EmploymentStartDate;
        entity.EmploymentClassification = dto.EmploymentClassification;
        entity.ExperienceYears = dto.ExperienceYears;
        entity.EmploymentRelationship = dto.EmploymentRelationship;
        entity.EmploymentStatus = dto.EmploymentStatus;
        entity.WeeklyHours = dto.WeeklyHours;
        entity.WorkScheduleDescription = dto.WorkScheduleDescription;
        entity.BreakDescription = dto.BreakDescription;
        entity.EducationLevel = dto.EducationLevel;
        entity.DigitalTimeOrganization = dto.DigitalTimeOrganization;
        entity.FullEmploymentHours = dto.FullEmploymentHours;
        entity.BreakMinutes = dto.BreakMinutes;
        entity.BreakWithinWorkingHours = dto.BreakWithinWorkingHours;
        entity.UsesDigitalWorkCard = dto.UsesDigitalWorkCard;
        entity.FlexibleScheduleMinutes = dto.FlexibleScheduleMinutes;
        entity.LastModifiedDate = dto.LastModifiedDate;
        entity.NormalDailyHours = dto.NormalDailyHours;
        entity.IsActive = dto.IsActive;

        await db.SaveChangesAsync();
    }

    public async Task SetActiveAsync(int id, bool isActive)
    {
        await using var db = OpenDb();
        var entity = await db.Employees.FindAsync(id)
            ?? throw new InvalidOperationException(ErganiManager.Data.UiText.F("Employee {0} not found.", id));
        entity.IsActive = isActive;
        await db.SaveChangesAsync();
    }

    private static void ValidateBasics(EmployeeDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.FirstName) || string.IsNullOrWhiteSpace(dto.LastName))
            throw new InvalidOperationException(ErganiManager.Data.UiText.T("First name and last name are required."));

        if (string.IsNullOrWhiteSpace(dto.TaxId) || dto.TaxId.Trim().Length != 9)
            throw new InvalidOperationException(ErganiManager.Data.UiText.T("Tax ID (AFM) must be exactly 9 digits."));

        if (string.IsNullOrWhiteSpace(dto.SocialSecurityNumber) || dto.SocialSecurityNumber.Trim().Length != 11)
            throw new InvalidOperationException(ErganiManager.Data.UiText.T("Social Security Number (AMKA) must be exactly 11 digits."));

        if (string.IsNullOrWhiteSpace(dto.BarcodeId))
            throw new InvalidOperationException(ErganiManager.Data.UiText.T("Barcode ID is required."));

        if (dto.WeeklyWorkdays is not (5 or 6))
            throw new InvalidOperationException(ErganiManager.Data.UiText.T("Weekly workdays must be 5 or 6."));

        if (dto.NormalDailyHours <= 0 || dto.NormalDailyHours > 24)
            throw new InvalidOperationException(ErganiManager.Data.UiText.T("Normal daily hours must be greater than 0 and no more than 24."));
    }

    private static EmployeeDto ToDto(Employee e) => new()
    {
        Id = e.Id,
        CompanyId = e.CompanyId,
        BranchId = e.BranchId,
        FirstName = e.FirstName,
        LastName = e.LastName,
        TaxId = e.TaxId,
        SocialSecurityNumber = e.SocialSecurityNumber,
        BarcodeId = e.BarcodeId,
        ProfessionCode = e.ProfessionCode,
        WeeklyWorkdays = e.WeeklyWorkdays,
        FatherName = e.FatherName,
        MotherName = e.MotherName,
        BirthDate = e.BirthDate,
        Sex = e.Sex,
        Nationality = e.Nationality,
        MaritalStatus = e.MaritalStatus,
        NumberOfChildren = e.NumberOfChildren,
        IdentityDocumentType = e.IdentityDocumentType,
        IdentityDocumentNumber = e.IdentityDocumentNumber,
        EmploymentStartDate = e.EmploymentStartDate,
        EmploymentClassification = e.EmploymentClassification,
        ExperienceYears = e.ExperienceYears,
        EmploymentRelationship = e.EmploymentRelationship,
        EmploymentStatus = e.EmploymentStatus,
        WeeklyHours = e.WeeklyHours,
        WorkScheduleDescription = e.WorkScheduleDescription,
        BreakDescription = e.BreakDescription,
        EducationLevel = e.EducationLevel,
        DigitalTimeOrganization = e.DigitalTimeOrganization,
        FullEmploymentHours = e.FullEmploymentHours,
        BreakMinutes = e.BreakMinutes,
        BreakWithinWorkingHours = e.BreakWithinWorkingHours,
        UsesDigitalWorkCard = e.UsesDigitalWorkCard,
        FlexibleScheduleMinutes = e.FlexibleScheduleMinutes,
        LastModifiedDate = e.LastModifiedDate,
        NormalDailyHours = e.NormalDailyHours,
        IsActive = e.IsActive
    };

    public async Task DeleteAsync(int id)
    {
        await using var db = OpenDb();
        var entity = await db.Employees.FindAsync(id);
        if (entity != null)
        {
            db.Employees.Remove(entity);
            await db.SaveChangesAsync();
        }
    }
}