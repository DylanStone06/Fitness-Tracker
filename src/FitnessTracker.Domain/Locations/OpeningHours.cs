using System.Collections.Frozen;
using FitnessTracker.Domain.Time;
using NodaTime;
using NodaTime.TimeZones;

namespace FitnessTracker.Domain.Locations;

/// <summary>
/// The hours a location is open on one weekday. A closing time at or before the opening time means after midnight,
/// on the next day (18:00 to 02:00). The same time means open until that time the next day: midnight to midnight is
/// the whole day, 23 or 25 hours on the days the clocks change.
/// </summary>
public readonly record struct OpenTimes(LocalTime Opens, LocalTime Closes);

/// <summary>
/// Why an activity doesn't fit a location's opening hours (H4): the day whose hours it should be inside, and those
/// hours, or null when the location is closed that day.
/// </summary>
public sealed record OpeningHoursProblem(LocalDate Date, OpenTimes? Hours);

/// <summary>
/// TRV-1 and H4: when a location is open, per weekday, including closed days and closing after midnight. Only the
/// activity part of a footprint has to be inside them; travel and buffers happen elsewhere (§7.1, T17).
/// Opening and closing times are clock times in the location's timezone. H4 is a hard rule, so on the nights the
/// clocks change an opening is never longer than the clock shows: a time the clocks skip means the moment they jump
/// past it, and a time that happens twice means the second one for opening and the first one for closing. (Series
/// use §8's rule instead, in <see cref="ClockTimes"/>: a skipped 01:30 becomes 02:30.)
/// </summary>
public sealed class OpeningHours
{
    private static readonly ZoneLocalMappingResolver OpeningResolver =
        Resolvers.CreateMappingResolver(Resolvers.ReturnLater, Resolvers.ReturnStartOfIntervalAfter);

    private static readonly ZoneLocalMappingResolver ClosingResolver =
        Resolvers.CreateMappingResolver(Resolvers.ReturnEarlier, Resolvers.ReturnStartOfIntervalAfter);

    private readonly FrozenDictionary<IsoDayOfWeek, OpenTimes> _days;

    /// <summary>Opening hours for the weekdays listed; a weekday that isn't listed is closed.</summary>
    public OpeningHours(IReadOnlyDictionary<IsoDayOfWeek, OpenTimes> days)
    {
        foreach (var day in days.Keys)
        {
            CalendarWeeks.RequireDay(day, nameof(days));
        }

        _days = days.ToFrozenDictionary();
    }

    private OpeningHours()
    {
        _days = FrozenDictionary<IsoDayOfWeek, OpenTimes>.Empty;
        IsAlwaysOpen = true;
    }

    /// <summary>A location without opening hours (home, a park): always open.</summary>
    public static OpeningHours Always { get; } = new();

    /// <summary>The weekdays with opening hours. Empty both for <see cref="Always"/> and for "closed every day".</summary>
    public IReadOnlyDictionary<IsoDayOfWeek, OpenTimes> Days => _days;

    /// <summary>True only for <see cref="Always"/>, so stored hours can tell "none entered" from "closed every day".</summary>
    public bool IsAlwaysOpen { get; }

    /// <summary>
    /// Null when the whole activity is inside the opening hours (touching closing time is fine: half-open). Otherwise
    /// the day whose hours it should be inside: the day of the opening it starts in, or, if it starts while closed, the
    /// day it starts.
    /// </summary>
    public OpeningHoursProblem? ProblemWith(Interval activity, DateTimeZone zone)
    {
        if (!activity.HasStart || !activity.HasEnd)
        {
            throw new ArgumentException("An activity needs both a start and an end.", nameof(activity));
        }

        if (IsAlwaysOpen)
        {
            return null;
        }

        var openings = OpeningsAround(activity, zone);
        if (Merge(openings.Select(opening => opening.Time)).Any(open => open.Start <= activity.Start && activity.End <= open.End))
        {
            return null;
        }

        foreach (var opening in openings)
        {
            if (opening.Time.Contains(activity.Start))
            {
                return new OpeningHoursProblem(opening.Date, opening.Hours);
            }
        }

        var startDate = activity.Start.InZone(zone).Date;
        return new OpeningHoursProblem(startDate, _days.TryGetValue(startDate.DayOfWeek, out var hours) ? hours : null);
    }

    // Every opening that could overlap the activity: from the day before it starts (an opening that closes after
    // midnight) to the day it ends, in time order.
    private List<(LocalDate Date, OpenTimes Hours, Interval Time)> OpeningsAround(Interval activity, DateTimeZone zone)
    {
        var openings = new List<(LocalDate, OpenTimes, Interval)>();
        var lastDate = activity.End.InZone(zone).Date;
        for (var date = activity.Start.InZone(zone).Date.PlusDays(-1); date <= lastDate; date = date.PlusDays(1))
        {
            if (_days.TryGetValue(date.DayOfWeek, out var hours) && OpenOn(date, hours, zone) is { } open)
            {
                openings.Add((date, hours, open));
            }
        }

        return openings;
    }

    // Null when the opening has no length once the clocks change: 01:45 to 02:00 on the night they go forward.
    private static Interval? OpenOn(LocalDate date, OpenTimes hours, DateTimeZone zone)
    {
        var closingDate = hours.Closes > hours.Opens ? date : date.PlusDays(1);
        var opens = date.At(hours.Opens).InZone(zone, OpeningResolver).ToInstant();
        var closes = closingDate.At(hours.Closes).InZone(zone, ClosingResolver).ToInstant();
        return closes > opens ? new Interval(opens, closes) : null;
    }

    // Openings that overlap or touch (Monday and Tuesday, each midnight to midnight) count as one long opening.
    private static List<Interval> Merge(IEnumerable<Interval> openings)
    {
        var merged = new List<Interval>();
        foreach (var opening in openings.OrderBy(opening => opening.Start))
        {
            if (merged.Count > 0 && opening.Start <= merged[^1].End)
            {
                merged[^1] = new Interval(merged[^1].Start, Instant.Max(merged[^1].End, opening.End));
            }
            else
            {
                merged.Add(opening);
            }
        }

        return merged;
    }
}
