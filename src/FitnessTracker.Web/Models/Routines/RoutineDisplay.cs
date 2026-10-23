using FitnessTracker.Domain.Time;
using FitnessTracker.Web.Data;
using NodaTime;

namespace FitnessTracker.Web.Models.Routines;

/// <summary>How the pages describe a routine (CAL-9), in words.</summary>
public static class RoutineDisplay
{
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
    /// T18: which week of its cycle <paramref name="today"/> is in, or null for a routine that is the same every week.
    /// Said plainly for the usual routine, which is in use; for another, as what it would be.
    /// </summary>
    public static string? ThisWeek(Routine routine, LocalDate today) => routine.CycleWeeks == 1
        ? null
        : routine.IsUsual
            ? $"This week is week {routine.ToCycle().WeekOf(today)}"
            : $"By its cycle, this week would be week {routine.ToCycle().WeekOf(today)}";
}
