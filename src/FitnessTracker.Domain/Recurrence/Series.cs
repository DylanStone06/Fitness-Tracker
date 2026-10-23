using System.Collections.Frozen;
using FitnessTracker.Domain.Time;
using NodaTime;

namespace FitnessTracker.Domain.Recurrence;

/// <summary>
/// One instance of a series: the date its rule puts it on, which identifies it even after it is moved, and the
/// exact time it takes up.
/// </summary>
public readonly record struct Occurrence(LocalDate Date, Interval Time);

/// <summary>A one-off change to one occurrence of a series (CAL-4), stored against the date its rule put it on.</summary>
public abstract record OccurrenceChange
{
    private OccurrenceChange()
    {
    }

    public sealed record Cancelled : OccurrenceChange;

    /// <summary>Moved or resized: a new clock time, in the series' timezone, and a new length.</summary>
    public sealed record Moved(LocalDateTime Start, Duration Length) : OccurrenceChange;
}

/// <summary>
/// The result of editing a whole series. One-off changes are kept where the edited series still falls on their
/// date and removed otherwise; both are counted so the user can be told (CAL-4, principle P5).
/// </summary>
public sealed record SeriesEdit(Series Series, int KeptChanges, int RemovedChanges);

/// <summary>
/// A repeating activity, defined in clock time plus a timezone (requirements §8). It starts at the same clock time
/// on every date its rule falls on, using the clock-change rules in <see cref="ClockTimes"/>, and lasts the same
/// elapsed time, except for occurrences with a one-off change. It can be limited to some of those dates
/// (<see cref="OnlyOn"/>): an activity in one week of a routine's cycle (CAL-9).
/// </summary>
public sealed class Series
{
    // Frozen, so a series can't be changed once made: every change returns a new series.
    private readonly FrozenDictionary<LocalDate, OccurrenceChange> _changes;

    // The dates it may happen on, of those its rule falls on; null for all of them.
    private readonly Func<LocalDate, bool>? _happensOn;

    public Series(LocalDate firstDate, LocalTime startTime, Duration length, DateTimeZone zone, RecurrenceRule rule)
        : this(firstDate, startTime, length, zone, rule, FrozenDictionary<LocalDate, OccurrenceChange>.Empty, happensOn: null)
    {
    }

    private Series(
        LocalDate firstDate, LocalTime startTime, Duration length, DateTimeZone zone, RecurrenceRule rule,
        FrozenDictionary<LocalDate, OccurrenceChange> changes, Func<LocalDate, bool>? happensOn)
    {
        FirstDate = firstDate;
        StartTime = startTime;
        Length = length;
        Zone = zone;
        Rule = rule;
        _changes = changes;
        _happensOn = happensOn;
    }

    public LocalDate FirstDate { get; }

    public LocalTime StartTime { get; }

    public Duration Length { get; }

    public DateTimeZone Zone { get; }

    public RecurrenceRule Rule { get; }

    /// <summary>One-off changes, by the date the rule put each changed occurrence on.</summary>
    public IReadOnlyDictionary<LocalDate, OccurrenceChange> Changes => _changes;

    /// <summary>
    /// The occurrences that start on dates from <paramref name="from"/> to <paramref name="to"/>, inclusive, in
    /// order. An occurrence belongs to the day it starts (requirements §8), so one starting the evening before
    /// <paramref name="from"/> isn't included even if it runs past midnight: to work out a day's load, include the
    /// day before too. A moved occurrence is included by the day it now starts, if the date it was moved from is one
    /// the series happens on (<see cref="OnlyOn"/>).
    /// </summary>
    public IReadOnlyList<Occurrence> OccurrencesBetween(LocalDate from, LocalDate to)
    {
        var occurrences = Rule.Dates(FirstDate, from, through: to)
            .Where(date => !Changes.ContainsKey(date) && Allows(date))
            .Select(date => OccurrenceAt(date, date.At(StartTime), Length))
            .ToList();

        foreach (var (date, change) in Changes)
        {
            if (change is OccurrenceChange.Moved move && move.Start.Date >= from && move.Start.Date <= to && Allows(date))
            {
                occurrences.Add(OccurrenceAt(date, move.Start, move.Length));
            }
        }

        return occurrences.OrderBy(occurrence => occurrence.Time.Start).ToList();
    }

    /// <summary>Whether it has an occurrence on <paramref name="date"/>: its rule falls on it, and it may happen then.</summary>
    public bool FallsOn(LocalDate date) => Rule.FallsOn(firstDate: FirstDate, date: date) && Allows(date);

    /// <summary>
    /// The same series, happening only on the dates its rule falls on that <paramref name="happensOn"/> allows (CAL-9:
    /// week A only), instead of any dates allowed before; null allows them all again. One-off changes are kept, by the
    /// date the rule put each on: a change on a date left out isn't shown, and shows again if that date is allowed again.
    /// </summary>
    public Series OnlyOn(Func<LocalDate, bool>? happensOn) => new(FirstDate, StartTime, Length, Zone, Rule, _changes, happensOn);

    /// <summary>The same series with one occurrence cancelled; the others are untouched.</summary>
    public Series CancelOccurrence(LocalDate date) => WithChange(date, new OccurrenceChange.Cancelled());

    /// <summary>The same series with one occurrence moved or resized; the others are untouched.</summary>
    public Series MoveOccurrence(LocalDate date, LocalDateTime newStart, Duration newLength) =>
        WithChange(date, new OccurrenceChange.Moved(newStart, newLength));

    /// <summary>The same series with one occurrence's one-off change undone, so it follows the series again.</summary>
    public Series RestoreOccurrence(LocalDate date)
    {
        RequireOccurrenceOn(date);
        return WithChanges(_changes.Where(change => change.Key != date).ToFrozenDictionary());
    }

    /// <summary>
    /// The whole series with a new first date, clock time, length or rule, on the dates it is allowed to happen on
    /// now (<see cref="OnlyOn"/>). One-off changes are kept for every date the edited series still falls on (T8); the
    /// result says how many were kept and how many were removed. A move to what is now the usual time and length is no
    /// longer a change, so it is neither: the occurrence simply follows the series, where it already was.
    /// </summary>
    public SeriesEdit Edit(LocalDate firstDate, LocalTime startTime, Duration length, RecurrenceRule rule)
    {
        var edited = new Series(firstDate, startTime, length, Zone, rule, FrozenDictionary<LocalDate, OccurrenceChange>.Empty, _happensOn);
        var removed = 0;
        foreach (var (date, change) in Changes)
        {
            if (!edited.FallsOn(date))
            {
                removed++;
                continue;
            }

            edited = edited.WithChange(date, change);
        }

        return new SeriesEdit(edited, KeptChanges: edited.Changes.Count, RemovedChanges: removed);
    }

    /// <summary>
    /// The same series in another timezone, keeping its clock times, one-off changes included (T6: "gym at 7am
    /// wherever I am").
    /// </summary>
    public Series InZone(DateTimeZone newZone) => new(FirstDate, StartTime, Length, newZone, Rule, _changes, _happensOn);

    private Series WithChange(LocalDate date, OccurrenceChange change)
    {
        RequireOccurrenceOn(date);

        // Moving an occurrence back to its usual time and length is no longer a one-off change. Compared as moments,
        // not clock times: on the night the clocks go forward, a 01:30 series really happens at 02:30.
        if (change is OccurrenceChange.Moved move && move.Length == Length
            && ClockTimes.ToInstant(move.Start, Zone) == ClockTimes.ToInstant(date.At(StartTime), Zone))
        {
            return RestoreOccurrence(date);
        }

        var changes = new Dictionary<LocalDate, OccurrenceChange>(_changes) { [date] = change };
        return WithChanges(changes.ToFrozenDictionary());
    }

    private void RequireOccurrenceOn(LocalDate date)
    {
        if (!FallsOn(date))
        {
            throw new ArgumentException($"The series has no occurrence on {date:uuuu-MM-dd}.", nameof(date));
        }
    }

    private Series WithChanges(FrozenDictionary<LocalDate, OccurrenceChange> changes) =>
        new(FirstDate, StartTime, Length, Zone, Rule, changes, _happensOn);

    private bool Allows(LocalDate date) => _happensOn?.Invoke(date) ?? true;

    private Occurrence OccurrenceAt(LocalDate date, LocalDateTime start, Duration length)
    {
        var startMoment = ClockTimes.ToInstant(start, Zone);
        return new Occurrence(date, new Interval(startMoment, startMoment + length));
    }
}
