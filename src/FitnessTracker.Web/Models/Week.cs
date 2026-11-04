using FitnessTracker.Web.Data;
using NodaTime;

namespace FitnessTracker.Web.Models;

/// <summary>The days of the week as the forms list them, Monday first, and as a stored set of days.</summary>
public static class Week
{
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
