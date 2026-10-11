using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ErganiManager.Core.Interfaces;
using ErganiManager.UI.Localization;

namespace ErganiManager.UI.ViewModels;

/// <summary>
/// One row of the daily status board. The data comes language-neutral from the service; every text is
/// built here from the language files, so it follows the selected language (call <see cref="Refresh"/>
/// when the language changes).
/// </summary>
public class TodayRowViewModel : ObservableObject
{
    private readonly ILocalizationService _loc;

    public TodayEmployeeStatusDto Dto { get; }

    public TodayRowViewModel(TodayEmployeeStatusDto dto, ILocalizationService loc)
    {
        Dto = dto;
        _loc = loc;
    }

    /// <summary>Re-reads every text (language changed).</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);

    public int EmployeeId => Dto.EmployeeId;
    public string FullName => Dto.FullName;
    public string BranchName => Dto.BranchName;
    public bool IsError => Dto.IsError;
    public bool IsWarning => Dto.IsWarning;
    public bool HasIssues => Dto.HasIssues;

    // ── texts ────────────────────────────────────────────────────────────

    public string ScheduleText => Dto.ScheduleKind switch
    {
        TodayScheduleKind.Leave  => $"🏖 {Dto.LeaveName}",
        TodayScheduleKind.Work   => $"{Time(Dto.PlannedStart)}–{Time(Dto.PlannedEnd)} {(Dto.IsHome ? "🏠" : "🏢")}",
        TodayScheduleKind.Rest   => _loc[L.TodayRestDay],
        TodayScheduleKind.Absent => _loc[L.TodayAbsent],
        _                        => _loc[L.TodayNoSchedule]
    };

    public string ArrivalText => Side(Dto.ArrivalTime, Dto.ArrivalExtra);
    public string DepartureText => Side(Dto.DepartureTime, Dto.DepartureExtra);
    public string ArrivalErgani => Send(Dto.ArrivalSend, Dto.ArrivalProtocol, Dto.ArrivalAttempts);
    public string DepartureErgani => Send(Dto.DepartureSend, Dto.DepartureProtocol, Dto.DepartureAttempts);

    public string PresenceText => Dto.Presence switch
    {
        TodayPresenceKind.Working    => string.Format(_loc[L.TodayPresWorking], Time(Dto.PresenceTime)),
        TodayPresenceKind.Left       => string.Format(_loc[L.TodayPresLeft], Time(Dto.PresenceTime)),
        TodayPresenceKind.OnLeave    => _loc[L.TodayPresOnLeave],
        TodayPresenceKind.NotArrived => _loc[L.TodayPresNotArrived],
        TodayPresenceKind.Expected   => string.Format(_loc[L.TodayPresExpected], Time(Dto.PresenceTime)),
        TodayPresenceKind.NotWorking => _loc[L.TodayPresNotWorking],
        _                            => "—"
    };

    public string IssuesText => Dto.Issues.Count == 0
        ? _loc[L.TodayOk]
        : string.Join("\n", IssueLines());

    /// <summary>Every movement of the day with its Ergani status, then the problems.</summary>
    public string DetailsText
    {
        get
        {
            var lines = new List<string>();
            foreach (var m in Dto.Movements)
                lines.Add($"{Time(m.Time)}  {(m.IsArrival ? _loc[L.TodayMoveIn] : _loc[L.TodayMoveOut]),-14}  {Send(m.SendState, m.Protocol, m.Attempts)}");
            if (lines.Count == 0)
                lines.Add(_loc[L.TodayNoMovements]);

            if (Dto.Issues.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add(_loc[L.TodayProblemsLabel]);
                lines.AddRange(IssueLines().Select(i => "• " + i));
            }
            return string.Join("\n", lines);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static string Time(System.TimeOnly? t) => t.HasValue ? t.Value.ToString("HH:mm") : "—";

    private static string Side(System.TimeOnly? time, int extra)
        => time.HasValue ? Time(time) + (extra > 0 ? $" (+{extra})" : string.Empty) : "—";

    private string Send(TodaySendState state, string? protocol, int attempts) => state switch
    {
        TodaySendState.Sent         => string.IsNullOrWhiteSpace(protocol)
                                        ? _loc[L.TodaySendSent]
                                        : string.Format(_loc[L.TodaySendSentProtocol], protocol),
        TodaySendState.Waiting      => _loc[L.TodaySendWaiting],
        TodaySendState.Retrying     => string.Format(_loc[L.TodaySendRetrying], attempts),
        TodaySendState.Failed       => _loc[L.TodaySendFailed],
        TodaySendState.NotSubmitted => _loc[L.TodaySendNotSubmitted],
        _                           => string.Empty
    };

    private IEnumerable<string> IssueLines() => Dto.Issues.Select(Describe).Distinct();

    private string Describe(TodayIssueDto i)
    {
        switch (i.Code)
        {
            case TodayIssueCode.SendProblem:
                var text = string.Format(_loc[i.IsArrival ? L.TodayIssueSendIn : L.TodayIssueSendOut],
                    Time(i.Time), Send(i.SendState, i.Protocol, i.Attempts));
                return string.IsNullOrWhiteSpace(i.Detail) ? text : $"{text} ({i.Detail})";
            case TodayIssueCode.LateClockIn:
                return string.Format(_loc[L.TodayIssueLate], Time(i.Time), Time(i.Planned), i.Minutes);
            case TodayIssueCode.NoClockIn:
                return string.Format(_loc[L.TodayIssueNoIn], Time(i.Planned));
            case TodayIssueCode.ClockOutWithoutIn:
                return _loc[L.TodayIssueOutNoIn];
            case TodayIssueCode.NoClockOut:
                return string.Format(_loc[L.TodayIssueNoOut], Time(i.Planned));
            case TodayIssueCode.NoClockOutPastDay:
                return _loc[L.TodayIssueNoOutPast];
            case TodayIssueCode.EarlyClockOut:
                return i.Minutes.HasValue
                    ? string.Format(_loc[L.TodayIssueEarlyOutMin], Time(i.Time), i.Minutes)
                    : string.Format(_loc[L.TodayIssueEarlyOut], Time(i.Time));
            case TodayIssueCode.DoubleClockIn:
                return string.Format(_loc[L.TodayIssueDoubleIn], Time(i.Time), Time(i.Time2));
            case TodayIssueCode.DoubleClockOut:
                return string.Format(_loc[L.TodayIssueDoubleOut], Time(i.Time), Time(i.Time2));
            case TodayIssueCode.ClockedOnLeave:
                return _loc[L.TodayIssueOnLeave];
            case TodayIssueCode.ClockedNoSchedule:
                return _loc[L.TodayIssueNoSchedule];
            default:
                return _loc[L.TodayIssueRestDay];
        }
    }
}
