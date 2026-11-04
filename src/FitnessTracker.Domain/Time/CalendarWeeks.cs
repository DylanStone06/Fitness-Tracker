using NodaTime;

namespace FitnessTracker.Domain.Time;

/// <summary>
/// Weeks that start on a chosen day (ACC-3's week start: Monday by default). Shared by "every N weeks" series and
/// A/B template cycles, so both agree on which week a date is in.
/// </summary>
public static class CalendarWeeks
{
    /// <summary>The first day of the week <paramref name="date"/> is in.</summary>
    public static LocalDate StartOf(LocalDate date, IsoDayOfWeek weekStart) => date.With(DateAdjusters.PreviousOrSame(weekStart));

    /// <summary>
    /// How many weeks the week of <paramref name="to"/> is after the week of <paramref name="from"/>: 0 for the same
    /// week, negative when it is before.
    /// </summary>
    public static int Between(LocalDate from, LocalDate to, IsoDayOfWeek weekStart) =>
        Period.Between(StartOf(from, weekStart), StartOf(to, weekStart), PeriodUnits.Days).Days / 7;

    /// <summary>Refuses anything but Monday to Sunday (NodaTime's IsoDayOfWeek also has None).</summary>
    public static void RequireDay(IsoDayOfWeek day, string parameterName)
    {
        if (day is < IsoDayOfWeek.Monday or > IsoDayOfWeek.Sunday)
        {
            throw new ArgumentOutOfRangeException(parameterName, day, "Days must be Monday to Sunday.");
        }
    }
}
