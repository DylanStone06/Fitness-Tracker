using NodaTime;
using NodaTime.TimeZones;

namespace FitnessTracker.Domain.Time;

/// <summary>
/// Requirements §8: turning a clock time on a date into an exact moment. On the nights the clocks change, a time
/// the clocks skip (going forward) moves forward by the gap, so 01:30 becomes 02:30, and a time that happens twice
/// (going back) means the first, earlier one.
/// </summary>
public static class ClockTimes
{
    public static Instant ToInstant(LocalDateTime clockTime, DateTimeZone zone) =>
        clockTime.InZone(zone, Resolvers.LenientResolver).ToInstant();

    /// <summary>
    /// The span between two clock times, half-open [start, end). Its length is the time that really passes, so the
    /// same clock times make a longer or shorter span on the nights the clocks change.
    /// </summary>
    public static Interval ToInterval(LocalDateTime start, LocalDateTime end, DateTimeZone zone) =>
        new(ToInstant(start, zone), ToInstant(end, zone));
}
