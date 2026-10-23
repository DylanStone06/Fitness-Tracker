using FitnessTracker.Domain.Templates;
using FitnessTracker.Domain.Time;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models.Activities;
using NodaTime;
using NodaTime.Text;

namespace FitnessTracker.Web.Models.Routines;

/// <summary>How the pages describe a routine (CAL-9), in words.</summary>
public static class RoutineDisplay
{
    // "Mon 26 Oct", as Plan my week writes its days.
    private static readonly LocalDatePattern ShortDate = LocalDatePattern.CreateWithInvariantCulture("ddd d MMM");
    /// <summary>"The same every week", or "Repeats every 2 weeks: week A, then week B".</summary>
    public static string Cycle(Routine routine) => routine.CycleWeeks switch
    {
        1 => "The same every week",
        2 => "Repeats every 2 weeks: week A, then week B",
        var weeks => $"Repeats every {weeks} weeks: weeks {Letters(weeks)} in turn",
    };

    /// <summary>"week A, B and C" style: the cycle's letters, written out.</summary>
    public static string Letters(int weeks)
    {
        var letters = Enumerable.Range(0, weeks).Select(week => ((char)('A' + week)).ToString()).ToList();
        return letters.Count == 1 ? letters[0] : $"{string.Join(", ", letters[..^1])} and {letters[^1]}";
    }

    /// <summary>"Week A is the week of Mon 2 Nov 2026".</summary>
    public static string WeekA(Routine routine) => $"Week A is the week of {DateText.Named.Format(routine.WeekAStarts)}";

    /// <summary>
    /// CAL-9, "each week shows its template and cycle week": "Usual week, week B" for days all in one routine and
    /// week, or each part with its dates ("Sun 25 Oct: Usual week, week A; Mon 26 Oct – Sat 31 Oct: Usual week, week
    /// B"). A routine that is the same every week is just its name. Null for a user with no routines.
    /// </summary>
    public static string? InUse(IReadOnlyList<RoutineRun> runs, IReadOnlyList<Routine> routines)
    {
        if (routines.Count == 0 || runs.Count == 0)
        {
            return null;
        }

        var byId = routines.ToDictionary(routine => routine.Id);
        string Name(RoutineWeek? inUse) => inUse is { } week && byId.TryGetValue(week.Routine, out var routine)
            ? routine.CycleWeeks > 1 ? $"{routine.Name}, week {week.Week}" : routine.Name
            : "no routine";

        if (runs.Count == 1)
        {
            return runs[0].InUse is null ? "No routine" : Name(runs[0].InUse);
        }

        return string.Join("; ", runs.Select(run => $"{Dates(run.Dates)}: {Name(run.InUse)}"));
    }

    /// <summary>
    /// SCH-4, Q12 with CAL-9: the weeks a session planned for <paramref name="date"/> is added to "every week": the week
    /// of the routine in use then ("…/B"), or whenever a routine that is the same every week is in use, so planning
    /// week B builds week B; every week without a routine. With the button's words: "Every week B".
    /// </summary>
    public static (string Weeks, string Label) EveryWeekFor(LocalDate date, RoutineCalendar calendar, IReadOnlyList<Routine> routines)
    {
        if (calendar.On(date) is not { } inUse || routines.FirstOrDefault(routine => routine.Id == inUse.Routine) is not { } routine)
        {
            return ("", "Every week");
        }

        return routine.CycleWeeks > 1
            ? (ActivityFormViewModel.WeeksValue(routine.Id, inUse.Week), $"Every week {inUse.Week}")
            : (ActivityFormViewModel.WeeksValue(routine.Id, null), routine.IsUsual ? "Every week" : $"Whenever {routine.Name} is in use");
    }

    private static string Dates(DateInterval dates) => dates.Start == dates.End
        ? ShortDate.Format(dates.Start)
        : $"{ShortDate.Format(dates.Start)} – {ShortDate.Format(dates.End)}";

    /// <summary>
    /// T18: which week of its cycle <paramref name="today"/> is in, or null for a routine that is the same every week.
    /// Said plainly for the usual routine, which is in use; for another, as what it would be.
    /// </summary>
    public static string? ThisWeek(Routine routine, LocalDate today) => routine.CycleWeeks == 1
        ? null
        : routine.IsUsual
            ? $"This week is week {routine.ToCycle().WeekOf(today)}"
            : $"By its cycle, this week would be week {routine.ToCycle().WeekOf(today)}";
}
