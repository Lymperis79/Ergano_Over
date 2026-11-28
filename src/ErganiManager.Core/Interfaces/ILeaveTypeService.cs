using ErganiManager.Core.Models;

namespace ErganiManager.Core.Interfaces;

/// <summary>Loads and persists the shared Ergani leave-type catalog.</summary>
public interface ILeaveTypeService
{
    IReadOnlyList<LeaveTypeInfo> All { get; }
    void Refresh();
    void Save(IEnumerable<LeaveTypeInfo> types);
    LeaveTypeInfo? Find(string? code);
    string Label(string? code);
}
