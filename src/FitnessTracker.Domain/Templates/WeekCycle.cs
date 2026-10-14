using FitnessTracker.Domain.Time;
using NodaTime;

namespace FitnessTracker.Domain.Templates;

/// <summary>
/// CAL-9 and T18: a routine template's cycle of 1 to 4 weeks (A/B weeks). Week A is the week the anchor date is
/// in (by default the week the template is first applied, requirements Q3); the cycle repeats forwards and
/// backwards from there.
/// </summary>
public sealed class WeekCycle
{
    public const int MaxWeeks = 4;

    public WeekCycle(int weeks, LocalDate anchor, IsoDayOfWeek weekStart = IsoDayOfWeek.Monday)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(weeks, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(weeks, MaxWeeks);
        CalendarWeeks.RequireDay(weekStart, nameof(weekStart));
        Weeks = weeks;
        Anchor = anchor;
        WeekStart = weekStart;
    }

    public int Weeks { get; }

    public LocalDate Anchor { get; }

    public IsoDayOfWeek WeekStart { get; }

    /// <summary>Which week of the cycle <paramref name="date"/> is in: 'A' for the anchor's week, then 'B', 'C', 'D'.</summary>
    public char WeekOf(LocalDate date)
    {
        var weeksFromAnchor = CalendarWeeks.Between(Anchor, date, WeekStart);
        // C#'s % keeps the sign, so it is wrapped into 0..Weeks-1 for weeks before the anchor too.
        var index = ((weeksFromAnchor % Weeks) + Weeks) % Weeks;
        return (char)('A' + index);
    }
}
