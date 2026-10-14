using System.Collections.Frozen;
using FitnessTracker.Domain.Time;
using NodaTime;

namespace FitnessTracker.Domain.Recurrence;

public enum RepeatFrequency
{
    Daily,
    Weekly,
}

/// <summary>
/// CAL-3: how a series repeats. Daily, or weekly on chosen days; every N days or weeks; ending never, on a date,
/// or after a number of occurrences. This is the part of iCalendar's RRULE the MVP supports (ADR 0001), with one
/// difference: a series' first date only counts if the rule falls on it.
/// </summary>
public sealed class RecurrenceRule
{
    private RecurrenceRule(
        RepeatFrequency frequency, int every, IReadOnlySet<IsoDayOfWeek> days, IsoDayOfWeek weekStart, LocalDate? lastDate, int? count)
    {
        Frequency = frequency;
        Every = every;
        Days = days;
        WeekStart = weekStart;
        LastDate = lastDate;
        Count = count;
    }

    public RepeatFrequency Frequency { get; }

    /// <summary>Every how many days (daily) or weeks (weekly).</summary>
    public int Every { get; }

    /// <summary>Weekly only: the days of the week it falls on.</summary>
    public IReadOnlySet<IsoDayOfWeek> Days { get; }

    /// <summary>
    /// The day a week starts on, which decides the weeks that "every N weeks" falls in. Kept with the rule
    /// (iCalendar's WKST), so changing the week-start preference later doesn't move existing series to other weeks.
    /// </summary>
    public IsoDayOfWeek WeekStart { get; }

    /// <summary>The last date it can fall on (inclusive), or null.</summary>
    public LocalDate? LastDate { get; }

    /// <summary>How many occurrences it has in all, counted from the series' first date, or null.</summary>
    public int? Count { get; }

    public static RecurrenceRule Daily(int everyDays = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(everyDays, 1);
        return new(RepeatFrequency.Daily, everyDays, FrozenSet<IsoDayOfWeek>.Empty, IsoDayOfWeek.Monday, lastDate: null, count: null);
    }

    public static RecurrenceRule Weekly(IEnumerable<IsoDayOfWeek> days, int everyWeeks = 1, IsoDayOfWeek weekStart = IsoDayOfWeek.Monday)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(everyWeeks, 1);
        // Frozen, so a rule (which may be shared by several series) can't be changed once made.
        var daySet = days.ToFrozenSet();
        if (daySet.Count == 0)
        {
            throw new ArgumentException("A weekly rule needs at least one day.", nameof(days));
        }

        foreach (var day in daySet)
        {
            CalendarWeeks.RequireDay(day, nameof(days));
        }

        CalendarWeeks.RequireDay(weekStart, nameof(weekStart));

        return new(RepeatFrequency.Weekly, everyWeeks, daySet, weekStart, lastDate: null, count: null);
    }

    /// <summary>The same rule, ending on <paramref name="lastDate"/> (inclusive) instead of any other ending.</summary>
    public RecurrenceRule EndingOn(LocalDate lastDate) => new(Frequency, Every, Days, WeekStart, lastDate, count: null);

    /// <summary>The same rule, ending after <paramref name="count"/> occurrences instead of any other ending.</summary>
    public RecurrenceRule EndingAfter(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        return new(Frequency, Every, Days, WeekStart, lastDate: null, count);
    }

    /// <summary>
    /// The dates the rule falls on from <paramref name="from"/> to <paramref name="through"/>, inclusive and in
    /// order, for a series starting on <paramref name="firstDate"/>. Without a count it starts at
    /// <paramref name="from"/>, because whether a date matches never depends on the dates before it; with a count it
    /// walks from the first date, because the count is counted from there.
    /// </summary>
    public IEnumerable<LocalDate> Dates(LocalDate firstDate, LocalDate from, LocalDate through)
    {
        var lastDate = LastDate is { } end && end < through ? end : through;
        var start = Count is null && from > firstDate ? from : firstDate;
        var found = 0;
        for (var date = start; date <= lastDate; date = date.PlusDays(1))
        {
            if (!Matches(date, firstDate))
            {
                continue;
            }

            if (date >= from)
            {
                yield return date;
            }

            if (++found == Count)
            {
                yield break;
            }
        }
    }

    /// <summary>Whether the rule falls on <paramref name="date"/> for a series starting on <paramref name="firstDate"/>.</summary>
    public bool FallsOn(LocalDate firstDate, LocalDate date) => Dates(firstDate, from: date, through: date).Any();

    // The repeating pattern alone, without the first date's own limits, the end date or the count.
    private bool Matches(LocalDate date, LocalDate firstDate) => Frequency switch
    {
        RepeatFrequency.Daily => DaysBetween(firstDate, date) % Every == 0,
        _ => Days.Contains(date.DayOfWeek) && CalendarWeeks.Between(firstDate, date, WeekStart) % Every == 0,
    };

    private static int DaysBetween(LocalDate start, LocalDate end) => Period.Between(start, end, PeriodUnits.Days).Days;
}
