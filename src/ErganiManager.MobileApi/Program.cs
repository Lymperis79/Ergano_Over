using System.Security.Claims;
using ErganiManager.Core;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.Core.Services;
using ErganiManager.ErganiApi;
using ErganiManager.Licensing;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using Microsoft.EntityFrameworkCore;
using ErganiManager.LocalCache;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;

// The API runs in a separate process from the desktop UI; load the same machine-bound licence before Core checks menu access.
LoadMobileLicense();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5000);

    options.ListenAnyIP(5001, listenOptions =>
    {
        listenOptions.UseHttps(
            @"C:\Users\ThinkPad\ErganiMobileApi.pfx",
            "ErganiDev2026!");
    });
});

// This host is intended to run beside the existing Ergani II installation and
// use its configured database. It deliberately reuses Core services.
builder.Services.AddLogging();
builder.Services.AddErganiManagerCore();
builder.Services.AddErganiApiClient();

// A server can handle many mobile users concurrently. The desktop app uses a
// process-wide context; the API must instead have one context per HTTP request.
builder.Services.RemoveAll<ICompanyContext>();
builder.Services.AddScoped<ICompanyContext, CompanyContextService>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "__Host-ErganiMobile";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Path = "/";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseHsts();

app.UseHttpsRedirection();
app.UseAuthentication();

// Rehydrate the existing Core session model for this request. Core services
// continue to enforce company scoping using ICompanyContext.
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var contextService = context.RequestServices.GetRequiredService<ICompanyContext>();
        var userId = int.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : 0;
        var roleText = context.User.FindFirstValue(ClaimTypes.Role);
        var companyIdText = context.User.FindFirstValue("company_id");
        var branchIdText = context.User.FindFirstValue("branch_id");
        var session = new UserSession
        {
            UserId = userId,
            Username = context.User.Identity.Name ?? string.Empty,
            Role = Enum.TryParse<AppUserRole>(roleText, out var role) ? role : AppUserRole.Limited,
            CompanyId = int.TryParse(companyIdText, out var cid) ? cid : null,
            CompanyName = context.User.FindFirstValue("company_name"),
            BranchId = int.TryParse(branchIdText, out var bid) ? bid : null,
            BranchName = context.User.FindFirstValue("branch_name"),
            AllowedMenus = MenuKeys.Parse(context.User.FindFirstValue("allowed_menus")),
            IsOfflineSession = false
        };
        contextService.SetSession(session);

        // Keep a super-admin's identity (CompanyId == null) separate from the
        // company currently selected for this request.
        var activeCompanyIdText = context.User.FindFirstValue("active_company_id");
        var activeCompanyName = context.User.FindFirstValue("active_company_name");
        if (session.IsSuperAdmin &&
            int.TryParse(activeCompanyIdText, out var activeCompanyId) &&
            !string.IsNullOrWhiteSpace(activeCompanyName))
        {
            contextService.SwitchCompany(activeCompanyId, activeCompanyName);
        }
    }

    await next();
});

app.UseAuthorization();

app.MapGet("/api/mobile/health", () => Results.Ok(new { status = "ok", service = "ErganiManager.MobileApi" }));

app.MapPost("/api/mobile/login", async Task<IResult> (LoginRequest request, IAuthService auth, HttpContext http) =>
{
    if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        return Results.BadRequest(new { message = "Username and password are required." });

    var result = await auth.LoginAsync(request.Username, request.Password);
    if (!result.Success || result.Session is null)
        return Results.Unauthorized();

    // Offline-cache login is intentionally not accepted for remote access.
    // A mobile client must reach the authoritative company database.
    if (result.Session.IsOfflineSession)
    {
        auth.Logout();
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    var session = result.Session;
    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, session.UserId.ToString()),
        new(ClaimTypes.Name, session.Username),
        new(ClaimTypes.Role, session.Role.ToString())
    };
    if (session.CompanyId.HasValue) claims.Add(new("company_id", session.CompanyId.Value.ToString()));
    if (!string.IsNullOrWhiteSpace(session.CompanyName)) claims.Add(new("company_name", session.CompanyName));
    if (session.BranchId.HasValue) claims.Add(new("branch_id", session.BranchId.Value.ToString()));
    if (!string.IsNullOrWhiteSpace(session.BranchName)) claims.Add(new("branch_name", session.BranchName));
    if (session.AllowedMenus is not null) claims.Add(new("allowed_menus", string.Join(",", session.AllowedMenus)));

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Ok(new MobileSessionResponse(session.Username, session.Role.ToString(),
        session.CompanyId, session.CompanyName, session.BranchId, session.BranchName));
}).AllowAnonymous();

app.MapPost("/api/mobile/logout", async (HttpContext http, IAuthService auth) =>
{
    auth.Logout();
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/api/mobile/me", (ICompanyContext companyContext) =>
{
    var session = companyContext.CurrentSession!;
    return Results.Ok(new MobileSessionResponse(session.Username, session.Role.ToString(),
        session.CompanyId, session.CompanyName, session.BranchId, session.BranchName));
}).RequireAuthorization();

app.MapGet("/api/mobile/companies", async Task<IResult> (ICompanyContext context, ICompanyService companies) =>
{
    var session = context.CurrentSession!;
    if (!session.CanSee(MenuKeys.Companies)) return Results.Forbid();
    var all = await companies.GetAllAsync();
    var visible = session.IsSuperAdmin
        ? all.Where(c => c.IsActive)
        : all.Where(c => c.Id == session.CompanyId && c.IsActive);
    return Results.Ok(visible.Select(c => new MobileCompanyResponse(c.Id, c.Name, c.TaxId)));
}).RequireAuthorization();

app.MapPost("/api/mobile/company/select", async Task<IResult> (
    SelectMobileCompanyRequest request,
    ICompanyContext context,
    ICompanyService companies,
    HttpContext http) =>
{
    var session = context.CurrentSession!;
    var company = await companies.GetByIdAsync(request.CompanyId);
    if (company is null || !company.IsActive)
        return Results.NotFound(new { message = "The selected company does not exist or is inactive." });

    if (session.IsSuperAdmin)
    {
        // SwitchCompany enforces the Core super-admin rule. The selected tenant
        // is stored in separate claims so the user's super-admin identity remains intact.
        context.SwitchCompany(company.Id, company.Name);
        var claims = http.User.Claims
            .Where(c => c.Type is not ("active_company_id" or "active_company_name"))
            .ToList();
        claims.Add(new Claim("active_company_id", company.Id.ToString()));
        claims.Add(new Claim("active_company_name", company.Name));
        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
    }
    else if (session.CompanyId != company.Id)
    {
        return Results.Forbid();
    }

    return Results.Ok(new MobileCompanyResponse(company.Id, company.Name, company.TaxId));
}).RequireAuthorization();

app.MapGet("/api/mobile/employees", async Task<IResult> (ICompanyContext context, IEmployeeService employees) =>
{
    var session = context.CurrentSession!;
    if (!session.CanSee(MenuKeys.Employees)) return Results.Forbid();
    var companyId = context.ActiveCompanyId;
    if (!companyId.HasValue) return Results.Conflict(new { message = "Select a company before requesting company data." });

    var rows = await employees.GetByCompanyAsync(companyId.Value, activeOnly: true);
    return Results.Ok(rows.Select(e => new MobileEmployeeResponse(e.Id, e.BranchId, e.FirstName, e.LastName, e.IsActive)));
}).RequireAuthorization();

app.MapGet("/api/mobile/today", async Task<IResult> (ICompanyContext context, ITodayStatusService today,
    DateOnly? date, int? graceMinutes) =>
{
    var session = context.CurrentSession!;
    if (!session.CanSee(MenuKeys.Today)) return Results.Forbid();
    var companyId = context.ActiveCompanyId;
    if (!companyId.HasValue) return Results.Conflict(new { message = "Select a company before requesting company data." });

    var selectedDate = date ?? DateOnly.FromDateTime(DateTime.Today);
    var grace = Math.Clamp(graceMinutes ?? 10, 0, 240);
    var rows = await today.GetAsync(companyId.Value, selectedDate, grace);
    return Results.Ok(rows);
}).RequireAuthorization();


app.MapGet("/api/mobile/schedules/{employeeId:int}/{year:int}/{month:int}", async Task<IResult> (
    int employeeId, int year, int month, ICompanyContext context,
    IEmployeeService employees, IScheduleService schedules) =>
{
    var session = context.CurrentSession!;
    if (!session.CanSee(MenuKeys.Schedules)) return Results.Forbid();
    var companyId = context.ActiveCompanyId;
    if (!companyId.HasValue)
        return Results.Conflict(new { message = "Select a company before requesting company data." });
    if (year < 2000 || year > 2100 || month is < 1 or > 12)
        return Results.BadRequest(new { message = "Invalid calendar month." });

    var employee = await employees.GetByIdAsync(employeeId);
    if (employee is null || employee.CompanyId != companyId.Value || !employee.IsActive)
        return Results.NotFound(new { message = "The employee was not found in the active company." });

    var rows = await schedules.GetMonthAsync(employeeId, year, month);
    return Results.Ok(rows);
}).RequireAuthorization();

app.MapGet("/api/mobile/workcards/history", async Task<IResult> (
    ICompanyContext context,
    IWorkCardHistoryService history,
    IEmployeeService employees,
    DateOnly? fromDate,
    DateOnly? toDate,
    int? employeeId) =>
{
    var session = context.CurrentSession!;
    if (!session.CanSee(MenuKeys.WorkCards)) return Results.Forbid();

    var companyId = context.ActiveCompanyId;
    if (!companyId.HasValue)
        return Results.Conflict(new { message = "Select a company before requesting company data." });

    if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
        return Results.BadRequest(new { message = "The start date must be on or before the end date." });

    if (employeeId.HasValue)
    {
        var employee = await employees.GetByIdAsync(employeeId.Value);
        if (employee is null || employee.CompanyId != companyId.Value || !employee.IsActive)
            return Results.NotFound(new { message = "The employee was not found in the active company." });
    }

    var filter = new WorkCardHistoryFilter
    {
        FromDate = fromDate,
        ToDate = toDate,
        EmployeeId = employeeId
    };
    var rows = await history.GetAsync(companyId.Value, filter);
    return Results.Ok(rows);
}).RequireAuthorization();

app.MapPost("/api/mobile/workcards/scan", async Task<IResult> (
    MobileWorkCardScanRequest request,
    ICompanyContext context,
    ICacheSyncService cacheSync,
    ErganiRetryService retryService) =>
{
    var session = context.CurrentSession!;
    if (!session.CanSee(MenuKeys.WorkCards)) return Results.Forbid();
    var companyId = context.ActiveCompanyId;
    if (!companyId.HasValue)
        return Results.Conflict(new { message = "Select a company before scanning a work card." });
    if (string.IsNullOrWhiteSpace(request.ScanValue))
        return Results.BadRequest(new { message = "Enter an employee number or scan a barcode." });

    var raw = request.ScanValue.Trim();
    var afmCandidate = raw;
    if (raw.Contains(':'))
    {
        var afmPart = raw.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split(':', 2))
            .FirstOrDefault(parts => parts.Length == 2 &&
                parts[0].Trim().Equals("afm", StringComparison.OrdinalIgnoreCase));
        if (afmPart is { Length: 2 }) afmCandidate = afmPart[1].Trim();
    }
    var digits = new string(afmCandidate.Where(char.IsDigit).ToArray());
    var afm = digits.Length == 9 ? digits.PadLeft(9, '0') : null;

    var sync = await cacheSync.RefreshCacheFromMainDatabaseAsync(companyId.Value);
    if (!sync.Success)
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

    using var cache = LocalCacheDbContextFactory.Create();
    var employee = await cache.CachedEmployees.FirstOrDefaultAsync(e =>
        e.CompanyId == companyId.Value && e.IsActive &&
        ((afm != null && e.TaxId == afm) || e.BarcodeId == raw));
    if (employee is null)
        return Results.NotFound(new { message = "No active employee matches that AFM or barcode in the selected company." });

    var now = DateTime.Now;
    var settings = await cache.CachedCompanySettings.FindAsync(companyId.Value);
    var shift = await ShiftResolver.ResolveAsync(cache, employee.Id, now,
        settings?.EarlyClockInBlockMinutes ?? 0);
    var movement = shift.Movement;
    var schedule = shift.Schedule;
    var officialHoliday = movement == "Arrival" &&
        CompanyHolidayCalendar.IsEnabled(settings?.OfficialHolidayDatesJson, DateOnly.FromDateTime(now));

    if (!officialHoliday && movement == "Arrival" && schedule != null &&
        schedule.WorkType is 2 or 3 &&
        (settings == null || settings.BlockClockInOnRestOrAbsentDay))
    {
        var dayType = schedule.WorkType == 2 ? "rest day" : "absence day";
        return Results.Conflict(new { message = $"Clock-in is blocked because today is marked as a {dayType}." });
    }

    if (!officialHoliday && movement == "Arrival" && schedule == null &&
        settings?.BlockClockInWithoutSchedule == true)
        return Results.Conflict(new { message = "Clock-in is blocked because no schedule exists for today." });

    if (!officialHoliday && movement == "Arrival" && shift.ShiftStart is { } shiftStart &&
        settings is { EarlyClockInBlockMinutes: > 0 })
    {
        var earliest = shiftStart.AddMinutes(-settings.EarlyClockInBlockMinutes);
        if (now < earliest)
            return Results.Conflict(new { message = $"Clock-in is too early. Earliest allowed time is {earliest:HH:mm}." });
    }

    var duplicateSince = now.AddSeconds(-20);
    var duplicate = await cache.PendingSubmissions.AnyAsync(p =>
        p.CompanyId == companyId.Value && p.EmployeeId == employee.Id &&
        p.ScannedAt >= duplicateSince && p.ScannedAt <= now.AddSeconds(1));
    if (duplicate)
        return Results.Conflict(new { message = "This employee was scanned in the last 20 seconds. Please wait before scanning again." });

    var queued = new PendingSubmission
    {
        EmployeeId = employee.Id,
        CompanyId = companyId.Value,
        BranchId = employee.BranchId,
        EmployeeBarcodeId = employee.BarcodeId,
        MovementType = movement,
        ScannedAt = now,
        Synced = false
    };
    cache.PendingSubmissions.Add(queued);
    await cache.SaveChangesAsync();

    // The host's existing ErganiRetryService owns delivery and retry ordering.
    // The mobile endpoint only writes to the shared queue, avoiding a second sender
    // racing the desktop application's retry worker.
    return Results.Ok(new MobileWorkCardScanResponse(
        employee.FullName, movement, now, "Queued",
        "Scan saved on the host. The existing Ergani retry service will submit it automatically."));
}).RequireAuthorization();

app.MapGet("/api/mobile/notifications", async Task<IResult> (
    ICompanyContext context, IConnectionStateService connectionState, long? afterId) =>
{
    var companyId = context.ActiveCompanyId;
    if (!companyId.HasValue)
        return Results.Conflict(new { message = "Select a company before requesting notifications." });

    await using var db = new AppDbContext(connectionState.GetDbOptions());
    var query = db.NotificationLogs.AsNoTracking()
        .Where(x => x.CompanyId == companyId.Value);
    if (afterId.HasValue && afterId.Value > 0)
        query = query.Where(x => x.Id > afterId.Value);

    var rows = await query.OrderBy(x => x.Id).Take(100)
        .Select(x => new MobileNotificationResponse(
            x.Id, x.EventCode, x.Title, x.Message, x.CreatedAt, x.IsRead))
        .ToListAsync();
    return Results.Ok(rows);
}).RequireAuthorization();

app.MapPost("/api/mobile/notifications/{notificationId:long}/read", async Task<IResult> (
    long notificationId, ICompanyContext context, IConnectionStateService connectionState) =>
{
    var companyId = context.ActiveCompanyId;
    if (!companyId.HasValue)
        return Results.Conflict(new { message = "Select a company before updating notifications." });

    await using var db = new AppDbContext(connectionState.GetDbOptions());
    var notification = await db.NotificationLogs.FirstOrDefaultAsync(
        x => x.Id == notificationId && x.CompanyId == companyId.Value);
    if (notification is null) return Results.NotFound();
    notification.IsRead = true;
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

app.Run();


static void LoadMobileLicense()
{
    if (!LicenseState.Enforced)
    {
        LicenseState.Current = null;
        return;
    }

    try
    {
        var appData = AppPaths.GetAppDataFolder();
        var keyPath = Path.Combine(appData, "license.key");
        var clockPath = Path.Combine(appData, "license.clock");
        var key = File.Exists(keyPath) ? File.ReadAllText(keyPath) : null;
        var today = DateOnly.FromDateTime(DateTime.Now);

        var result = LicenseCodec.Evaluate(
            key,
            LicenseKeys.PublicKeyBase64,
            MachineCode.Get(),
            today);

        // Apply the same clock rollback protection used by the desktop licence manager.
        if (result.IsValid && result.Payload?.ExpiresDate is not null && File.Exists(clockPath))
        {
            var parts = File.ReadAllText(clockPath).Trim().Split('|');
            if (parts.Length == 2 &&
                DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", null,
                    System.Globalization.DateTimeStyles.None, out var lastDay) &&
                SignLicenseClock(parts[0]) == parts[1] &&
                today < lastDay.AddDays(-1))
            {
                result = new LicenseResult(LicenseCheck.ClockTampered, result.Payload);
            }
        }

        LicenseState.Current = result.IsValid ? result.Payload : null;
        Console.WriteLine(result.IsValid
            ? "Mobile API licence validated."
            : "Mobile API licence validation failed: " + result.Check);
    }
    catch (Exception ex)
    {
        LicenseState.Current = null;
        Console.Error.WriteLine("Mobile API licence initialization failed: " + ex.Message);
    }
}

static string SignLicenseClock(string value)
{
    var machineKey = System.Text.Encoding.UTF8.GetBytes(
        MachineCode.Normalize(MachineCode.Get()));
    return Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(
        machineKey, System.Text.Encoding.UTF8.GetBytes(value)))[..16];
}


public sealed record LoginRequest(string Username, string Password);
public sealed record SelectMobileCompanyRequest(int CompanyId);
public sealed record MobileSessionResponse(string Username, string Role, int? CompanyId,
    string? CompanyName, int? BranchId, string? BranchName);
public sealed record MobileCompanyResponse(int Id, string Name, string TaxId);
public sealed record MobileEmployeeResponse(int Id, int BranchId, string FirstName, string LastName, bool IsActive);

public sealed record MobileNotificationResponse(long Id, int EventCode, string Title,
    string Message, DateTime CreatedAt, bool IsRead);

public sealed record MobileWorkCardScanRequest(string ScanValue);
public sealed record MobileWorkCardScanResponse(string EmployeeName, string Movement, DateTime ScannedAt, string Status, string Message);
