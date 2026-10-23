using NodaTime;

namespace FitnessTracker.Domain.Templates;

/// <summary>CAL-9: a routine chosen for a range of dates, inclusive: a holiday week, or a Brighton weekend (TRV-4).</summary>
public sealed record RoutineDates(Guid Routine, DateInterval Dates);

/// <summary>The routine in use on a date, and which week of its cycle the date is in: 'A' to 'D'.</summary>
public readonly record struct RoutineWeek(Guid Routine, char Week);

/// <summary>Consecutive dates with the same routine and week in use, or none (<see cref="InUse"/> null).</summary>
public sealed record RoutineRun(DateInterval Dates, RoutineWeek? InUse);

/// <summary>
/// CAL-9: which routine (a kind of week) is in use on each date, and which week of its cycle. A routine chosen for some
/// dates is in use on them; every other date uses the usual routine, if there is one. Each cycle counts weeks by the
/// calendar from its own week A, so it keeps counting through dates another routine is used on, as a rota does: the
/// week after a holiday in week B is week A.
/// </summary>
public sealed class RoutineCalendar
{
    private readonly IReadOnlyDictionary<Guid, WeekCycle> _cycles;
    private readonly Guid? _usual;
    private readonly List<RoutineDates> _dates;

    /// <param name="cycles">Each routine's cycle. Dates, or a usual routine, with no cycle here are left out.</param>
    /// <param name="usual">The routine in use on any date not chosen for another, or null for none.</param>
    /// <param name="dates">Routines chosen for ranges of dates. The pages refuse ranges that overlap; if any do, the one
    /// that starts later wins, then the shorter, so the answer is the same every time (SCH-7).</param>
    public RoutineCalendar(IReadOnlyDictionary<Guid, WeekCycle> cycles, Guid? usual, IEnumerable<RoutineDates> dates)
    {
        _cycles = cycles;
        _usual = usual is { } id && cycles.ContainsKey(id) ? id : null;
        _dates = dates
            .Where(chosen => cycles.ContainsKey(chosen.Routine))
            .OrderByDescending(chosen => chosen.Dates.Start)
            .ThenBy(chosen => chosen.Dates.End)
            .ThenBy(chosen => chosen.Routine)
            .ToList();
    }

    /// <summary>No routines: every activity happens on every date its rule falls on.</summary>
    public static RoutineCalendar None { get; } = new(new Dictionary<Guid, WeekCycle>(), usual: null, []);

    /// <summary>The routine in use on <paramref name="date"/> and its week, or null when none is.</summary>
    public RoutineWeek? On(LocalDate date)
    {
        var routine = _dates.FirstOrDefault(chosen => chosen.Dates.Contains(date))?.Routine ?? _usual;
        return routine is { } id ? new RoutineWeek(id, _cycles[id].WeekOf(date)) : null;
    }

    /// <summary>
    /// <paramref name="dates"/> in runs of the same routine and week, in date order: one run for an ordinary week, more
    /// where a routine chosen for some days starts or ends, or a new week of the cycle begins. For a page to say which
    /// routine each part of the days it shows is in.
    /// </summary>
    public IReadOnlyList<RoutineRun> Across(DateInterval dates)
    {
        var runs = new List<RoutineRun>();
        foreach (var date in dates)
        {
            var inUse = On(date);
            if (runs.Count > 0 && runs[^1].InUse == inUse)
            {
                runs[^1] = runs[^1] with { Dates = new DateInterval(runs[^1].Dates.Start, date) };
            }
            else
            {
                runs.Add(new RoutineRun(new DateInterval(date, date), inUse));
            }
        }

        return runs;
    }

    /// <summary>
    /// Whether an activity in <paramref name="routine"/> happens on <paramref name="date"/>: when that routine is in use
    /// then, in <paramref name="week"/> of its cycle, or in any week when <paramref name="week"/> is null.
    /// </summary>
    public bool Includes(Guid routine, char? week, LocalDate date) =>
        On(date) is { } inUse && inUse.Routine == routine && (week is null || week == inUse.Week);
}
