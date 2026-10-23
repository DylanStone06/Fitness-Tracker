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
    /// same clock times make a longer or shorter span on the nights the clocks change. A span that would end before
    /// it starts is refused with a reason (H8): for example 01:45 to 02:00 on the night the clocks go forward, where
    /// 01:45 moves to 02:45 but 02:00 exists.
    /// </summary>
    public static Interval ToInterval(LocalDateTime start, LocalDateTime end, DateTimeZone zone)
    {
        var startMoment = ToInstant(start, zone);
        var endMoment = ToInstant(end, zone);
        if (endMoment < startMoment)
        {
            throw new ArgumentException(
                $"{start:uuuu-MM-dd HH:mm} to {end:uuuu-MM-dd HH:mm} ends before it starts, once the clocks changing in {zone.Id} is allowed for.",
                nameof(end));
        }

        return new(startMoment, endMoment);
    }
}
