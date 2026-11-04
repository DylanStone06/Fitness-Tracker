using NodaTime;

namespace FitnessTracker.Domain.Time;

/// <summary>The part of an activity that falls on one calendar day.</summary>
public readonly record struct DayShare(LocalDate Day, Duration Length);

/// <summary>
/// Requirements §8 ("crossing midnight", T3): an activity belongs to the day it starts, for the calendar and
/// targets, but its minutes count towards each day's load for the part that falls on that day.
/// </summary>
public static class CalendarDays
{
    public static LocalDate DayOf(Interval activity, DateTimeZone zone) => activity.Start.InZone(zone).Date;

    public static IReadOnlyList<DayShare> Split(Interval activity, DateTimeZone zone)
    {
        var shares = new List<DayShare>();
        var day = DayOf(activity, zone);
        var shareStart = activity.Start;
        while (shareStart < activity.End)
        {
            // Measured to the next midnight, not as 24 hours: the day the clocks change lasts 23 or 25 hours.
            var nextMidnight = zone.AtStartOfDay(day.PlusDays(1)).ToInstant();
            var shareEnd = Instant.Min(nextMidnight, activity.End);
            shares.Add(new DayShare(day, shareEnd - shareStart));
            day = day.PlusDays(1);
            shareStart = nextMidnight;
        }

        return shares;
    }
}
