using System.Globalization;
using FitnessTracker.Web.Data;
using NodaTime;
using NodaTime.Text;

namespace FitnessTracker.Web.Models;

/// <summary>
/// The days of the week as the forms list them, Monday first, and as a stored set of days; and a week's dates as the
/// pages write them.
/// </summary>
public static class Week
{
    private static readonly LocalDatePattern FullDate = LocalDatePattern.CreateWithInvariantCulture("d MMM uuuu");

    private static readonly LocalDatePattern DayAndMonth = LocalDatePattern.CreateWithInvariantCulture("d MMM");

    /// <summary>"2 – 8 Nov 2026", "26 Oct – 1 Nov 2026" or "28 Dec 2026 – 3 Jan 2027": only what changes is repeated.</summary>
    public static string Range(DateInterval dates)
    {
        var (start, end) = (dates.Start, dates.End);
        var from = start.Year != end.Year ? FullDate.Format(start)
            : start.Month != end.Month ? DayAndMonth.Format(start)
            : start.Day.ToString(CultureInfo.InvariantCulture);
        return $"{from} – {FullDate.Format(end)}";
    }

    public static IReadOnlyList<IsoDayOfWeek> Days { get; } =
    [
        IsoDayOfWeek.Monday, IsoDayOfWeek.Tuesday, IsoDayOfWeek.Wednesday, IsoDayOfWeek.Thursday,
        IsoDayOfWeek.Friday, IsoDayOfWeek.Saturday, IsoDayOfWeek.Sunday,
    ];

    /// <summary>The days in a stored set, Monday first.</summary>
    public static IEnumerable<IsoDayOfWeek> DaysIn(DaysOfWeek days) => Days.Where(day => days.HasFlag(FlagFor(day)));

    /// <summary>The days as a stored set.</summary>
    public static DaysOfWeek SetOf(IEnumerable<IsoDayOfWeek> days) =>
        days.Where(day => day != IsoDayOfWeek.None).Aggregate(DaysOfWeek.None, (set, day) => set | FlagFor(day));

    // Monday is 1 in both IsoDayOfWeek and ISO-8601, so Monday's flag is 1, Tuesday's 2, and so on.
    private static DaysOfWeek FlagFor(IsoDayOfWeek day) => (DaysOfWeek)(1 << ((int)day - 1));
}
