using ErganiManager.Core.Models;

namespace ErganiManager.Core.Interfaces;

public interface IScheduleSubmitter
{
    Task<(int Submitted, string? Error)> SubmitScheduleDaysAsync(
        int companyId, IReadOnlyList<ScheduleDayDto> days);

    Task<(int Submitted, string? Error)> SubmitOvertimeDaysAsync(
        int companyId, IReadOnlyList<ScheduleDayDto> overtimeDays);
}
