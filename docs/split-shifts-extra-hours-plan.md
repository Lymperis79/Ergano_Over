# Split shifts and potential extra-hours review

## Behavior

- A schedule day can contain more than one continuous work period. The day editor currently exposes a first period and an optional second period; storage and the Ergani request model support a list of periods.
- Total planned hours are calculated by summing period durations. Gaps between periods are not counted as working time.
- Each employee has a configurable normal daily hours value (default 8 hours). The schedule editor compares planned hours with this value and shows a review warning for any excess.
- The warning is informational. The app does not infer that excess time is legally overtime and does not automatically create or submit an overtime declaration. The user must review the applicable declaration type and create the appropriate record from the overtime workflow.
- Daily schedule submission sends each configured work period as a separate work-time analytics interval.

## Data safety and compatibility

- Existing EmployeeSchedule.StartTime and EndTime columns remain in place for compatibility with old callers and records.
- A new ScheduleWorkingPeriods table stores intervals. Existing single-period schedules are copied into period 1 by the additive schema updater; the original columns and schedule/submission history are retained.
- Schedule cloning and clone undo preserve period rows.
- The new employee-hours column has a default of 8 so existing employee records continue with the previous normal-day assumption unless an administrator changes it.

## Validation and current scope

- Periods must have a positive duration and must not overlap.
- The day editor currently supports up to two periods per day. The storage and API models are list-based so more periods can be enabled later without adding more database columns.
- This change does not automatically determine the legally correct Greek labour/Ergani declaration category. The employee's normal daily hours are a review threshold only; legal classification remains a deliberate user decision.
- Overnight periods crossing midnight and a bulk editor for multiple periods are not included in this first implementation pass and should be added only with explicit date-boundary and Ergani-format validation.
