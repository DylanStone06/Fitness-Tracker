using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Domain.Time;
using FitnessTracker.Web.Data;
using NodaTime;

namespace FitnessTracker.Web.Services;

/// <summary>
/// SCH-1: runs the one check (<see cref="ActivityCheck"/>) on an activity about to be saved, against the signed-in
/// user's other activities, locations and travel times (<see cref="PlanningData"/>). Every way of saving an activity
/// calls this; the rules themselves are in the domain.
/// </summary>
public class ActivityChecker(PlanningData planning)
{
    /// <summary>
    /// The check's explanations write times as the user chose (ACC-3), or as <paramref name="clock"/> says, and the rest
    /// rules are the saved ones, or <paramref name="rest"/>: two checks whose findings are compared (before and after a
    /// change) must write times the same way and keep the same rules.
    /// </summary>
    public async Task<ActivityCheckResult> CheckAsync(Activity candidate, ClockFormat? clock = null, RestRules? rest = null)
    {
        var loaded = await planning.LoadAsync();
        var user = loaded with { Context = loaded.Context with { Clock = clock ?? loaded.Context.Clock, Rest = rest ?? loaded.Context.Rest } };

        // Only activities that can fall near the dates checked (for a demanding one, as far ahead as its rest rules are
        // checked and as far either side as the rest between demanding sessions reaches, H5-H7), not every one the user
        // ever had; one that can't be read is left out with a note, not allowed to stop every save. Each in the weeks its
        // routine allows (CAL-9), the candidate too.
        var plannedCandidate = candidate.ToPlannedActivity((await planning.RoutinesAsync()).Calendar);
        var unreadable = new List<string>();
        var nearby = ActivityCheck.OthersThatCanClash(plannedCandidate, user.Context.Today, user.Context.Rest.BetweenDemanding);
        var others = await planning.ActivitiesOnAsync(nearby, unreadable, except: candidate.Id);

        var result = ActivityCheck.Check(plannedCandidate, others, user.Context);
        return unreadable.Count == 0 ? result : result with { Notes = [.. unreadable, .. result.Notes] };
    }

    /// <summary>
    /// Q8, SCH-1: each of <paramref name="activities"/>' problems and clashes as the check on save sees them now, in plain
    /// English, by title ("Gym: On Mon 2 Nov 2026, …"). For changes that move activities without their own form (a
    /// timezone change, a routine chosen for some dates): run before and after, what is new is what the change broke.
    /// Both runs must write times the same way (<paramref name="clock"/>) and keep the same rest rules
    /// (<paramref name="rest"/>, or the saved ones), so a change of either alone doesn't make a finding look new.
    /// </summary>
    public async Task<List<string>> FindingsAsync(IEnumerable<Activity> activities, ClockFormat clock, RestRules? rest = null)
    {
        var findings = new List<string>();
        foreach (var activity in activities)
        {
            var result = await CheckAsync(activity, clock, rest);
            var zone = DateTimeZoneProviders.Tzdb[activity.TimeZoneId];
            findings.AddRange(result.Problems.Select(problem => $"{activity.Title}: {problem.Message}"));
            findings.AddRange(result.Clashes.Select(clash => $"{activity.Title}: {ActivityCheck.Describe(clash, zone, clock)}"));
        }

        return findings;
    }

    /// <summary>
    /// CAL-9, SCH-1: <see cref="FindingsAsync"/> for every activity in a routine, for a change to which routine is in use
    /// when (dates chosen for a routine, another made usual): run before and after it, what is new is what it broke.
    /// </summary>
    public async Task<List<string>> RoutineActivityFindingsAsync()
    {
        var clock = (await planning.LoadAsync()).Preferences.TimesShownAs();
        return await FindingsAsync(await planning.ActivitiesInRoutinesAsync(), clock);
    }

    /// <summary>
    /// CAL-4: the check for a change to one occurrence of <paramref name="candidate"/> (as it would be saved), on that
    /// occurrence alone, where it now is (<see cref="ActivityCheck.CheckOccurrence"/>). <paramref name="occurrenceDate"/>
    /// is the date its rule puts it on.
    /// </summary>
    public async Task<ActivityCheckResult> CheckOccurrenceAsync(Activity candidate, LocalDate occurrenceDate)
    {
        var user = await planning.LoadAsync();
        var plannedCandidate = candidate.ToPlannedActivity((await planning.RoutinesAsync()).Calendar);
        var unreadable = new List<string>();
        var nearby = ActivityCheck.OthersThatCanClashWith(plannedCandidate.Series, occurrenceDate, user.Context.Rest.BetweenDemanding);
        var others = await planning.ActivitiesOnAsync(nearby, unreadable, except: candidate.Id);

        var result = ActivityCheck.CheckOccurrence(plannedCandidate, occurrenceDate, others, user.Context);
        return unreadable.Count == 0 ? result : result with { Notes = [.. unreadable, .. result.Notes] };
    }
}
