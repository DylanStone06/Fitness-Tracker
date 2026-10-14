using FitnessTracker.Domain.Time;
using NodaTime;

namespace FitnessTracker.Domain.Recurrence;

/// <summary>One instance of a series: the date its rule puts it on, and the exact time it takes up.</summary>
public readonly record struct Occurrence(LocalDate Date, Interval Time);

/// <summary>
/// A repeating activity, defined in clock time plus a timezone (requirements §8). It starts at the same clock time
/// on every date its rule falls on, using the clock-change rules in <see cref="ClockTimes"/>, and lasts the same
/// elapsed time.
/// </summary>
public sealed class Series(LocalDate firstDate, LocalTime startTime, Duration length, DateTimeZone zone, RecurrenceRule rule)
{
    public LocalDate FirstDate { get; } = firstDate;

    public LocalTime StartTime { get; } = startTime;

    public Duration Length { get; } = length;

    public DateTimeZone Zone { get; } = zone;

    public RecurrenceRule Rule { get; } = rule;

    /// <summary>
    /// The occurrences that start on dates from <paramref name="from"/> to <paramref name="to"/>, inclusive. An
    /// occurrence belongs to the day it starts (requirements §8), so one starting the evening before
    /// <paramref name="from"/> isn't included even if it runs past midnight: to work out a day's load, include the
    /// day before too.
    /// </summary>
    public IReadOnlyList<Occurrence> OccurrencesBetween(LocalDate from, LocalDate to) =>
        Rule.Dates(FirstDate, through: to)
            .Where(date => date >= from)
            .Select(OccurrenceOn)
            .ToList();

    /// <summary>The same series in another timezone, keeping its clock times (T6: "gym at 7am wherever I am").</summary>
    public Series InZone(DateTimeZone newZone) => new(FirstDate, StartTime, Length, newZone, Rule);

    private Occurrence OccurrenceOn(LocalDate date)
    {
        var start = ClockTimes.ToInstant(date.At(StartTime), Zone);
        return new Occurrence(date, new Interval(start, start + Length));
    }
}
