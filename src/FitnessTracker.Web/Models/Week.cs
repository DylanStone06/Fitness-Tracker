using NodaTime;

namespace FitnessTracker.Web.Models;

/// <summary>The days of the week as the forms list them, Monday first.</summary>
public static class Week
{
    public static IReadOnlyList<IsoDayOfWeek> Days { get; } =
    [
        IsoDayOfWeek.Monday, IsoDayOfWeek.Tuesday, IsoDayOfWeek.Wednesday, IsoDayOfWeek.Thursday,
        IsoDayOfWeek.Friday, IsoDayOfWeek.Saturday, IsoDayOfWeek.Sunday,
    ];
}
