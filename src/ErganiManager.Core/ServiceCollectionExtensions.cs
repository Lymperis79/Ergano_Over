using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ErganiManager.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all ErganiManager.Core services. Note: IWorkCardSubmitter is
    /// NOT registered here — it must be supplied by the host application once
    /// the Ergani API client (a later phase) is implemented, since Core has no
    /// HTTP dependency by design.
    /// </summary>
    public static IServiceCollection AddErganiManagerCore(this IServiceCollection services)
    {
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<ICompanyContext, CompanyContextService>();
        services.AddSingleton<IConnectionStateService, ConnectionStateService>();
        services.AddTransient<IAuthService, AuthService>();
        services.AddTransient<ICacheSyncService, CacheSyncService>();
        services.AddTransient<IAdminBootstrapService, AdminBootstrapService>();
        services.AddTransient<ICompanyService, CompanyService>();
        services.AddSingleton<LicenseSeatService>();
        services.AddTransient<IBranchService, BranchService>();
        services.AddTransient<IEmployeeService, EmployeeService>();
        services.AddTransient<IUserManagementService, UserManagementService>();
        services.AddTransient<IScheduleService, ScheduleService>();
        services.AddTransient<IWorkCardHistoryService, WorkCardHistoryService>();
        services.AddTransient<ITodayStatusService, TodayStatusService>();
        services.AddTransient<IOvertimeService, OvertimeService>();
        services.AddTransient<IImportLogService, ImportLogService>();
        services.AddSingleton<IRoundingSettingsService, RoundingSettingsService>();
        services.AddSingleton<IPortalSettingsService, PortalSettingsService>();
        services.AddTransient<IReportService, ReportService>();
        services.AddTransient<ILeaveService, LeaveService>();
        services.AddTransient<IDatabaseMaintenanceService, DatabaseMaintenanceService>();
        services.AddTransient<IOfficialHolidayCatalogService, OfficialHolidayCatalogService>();

        return services;
    }
}
