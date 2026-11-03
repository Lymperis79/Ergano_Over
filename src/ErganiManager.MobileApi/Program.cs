using System.Security.Claims;
using ErganiManager.Core;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.Core.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

// This host is intended to run beside the existing Ergani II installation and
// use its configured database. It deliberately reuses Core services.
builder.Services.AddLogging();
builder.Services.AddErganiManagerCore();

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
    }

    await next();
});

app.UseAuthorization();

app.MapGet("/api/mobile/health", () => Results.Ok(new { status = "ok", service = "ErganiManager.MobileApi" }));

app.MapPost("/api/mobile/login", async (LoginRequest request, IAuthService auth, HttpContext http) =>
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

app.MapGet("/api/mobile/companies", async (ICompanyContext context, ICompanyService companies) =>
{
    var session = context.CurrentSession!;
    if (!session.CanSee(MenuKeys.Companies)) return Results.Forbid();
    var all = await companies.GetAllAsync();
    var visible = session.IsSuperAdmin
        ? all.Where(c => c.IsActive)
        : all.Where(c => c.Id == session.CompanyId && c.IsActive);
    return Results.Ok(visible.Select(c => new MobileCompanyResponse(c.Id, c.Name, c.TaxId)));
}).RequireAuthorization();

app.MapGet("/api/mobile/employees", async (ICompanyContext context, IEmployeeService employees) =>
{
    var session = context.CurrentSession!;
    if (!session.CanSee(MenuKeys.Employees)) return Results.Forbid();
    var companyId = context.ActiveCompanyId;
    if (!companyId.HasValue) return Results.Conflict(new { message = "Select a company before requesting company data." });

    var rows = await employees.GetByCompanyAsync(companyId.Value, activeOnly: true);
    return Results.Ok(rows.Select(e => new MobileEmployeeResponse(e.Id, e.BranchId, e.FirstName, e.LastName, e.IsActive)));
}).RequireAuthorization();

app.MapGet("/api/mobile/today", async (ICompanyContext context, ITodayStatusService today,
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

app.Run();

public sealed record LoginRequest(string Username, string Password);
public sealed record MobileSessionResponse(string Username, string Role, int? CompanyId,
    string? CompanyName, int? BranchId, string? BranchName);
public sealed record MobileCompanyResponse(int Id, string Name, string TaxId);
public sealed record MobileEmployeeResponse(int Id, int BranchId, string FirstName, string LastName, bool IsActive);
