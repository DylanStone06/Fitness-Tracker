using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Domain.Time;
using FitnessTracker.Web.Data;

namespace FitnessTracker.Web.Services;

/// <summary>
/// SCH-1: runs the one check (<see cref="ActivityCheck"/>) on an activity about to be saved, against the signed-in
/// user's other activities, locations and travel times (<see cref="PlanningData"/>). Every way of saving an activity
/// calls this; the rules themselves are in the domain.
/// </summary>
public class ActivityChecker(PlanningData planning)
{
    /// <summary>
    /// The check's explanations write times as the user chose (ACC-3), or as <paramref name="clock"/> says: two checks
    /// whose findings are compared (before and after a change) must write them the same way.
    /// </summary>
    public async Task<ActivityCheckResult> CheckAsync(Activity candidate, ClockFormat? clock = null)
    {
        var loaded = await planning.LoadAsync();
        var user = clock is { } chosen ? loaded with { Context = loaded.Context with { Clock = chosen } } : loaded;

        // Only activities that can fall near the dates checked, not every one the user ever had; one that can't be read
        // is left out with a note, not allowed to stop every save.
        var plannedCandidate = candidate.ToPlannedActivity();
        var unreadable = new List<string>();
        var others = await planning.ActivitiesOnAsync(ActivityCheck.OthersThatCanClash(plannedCandidate.Series, user.Context.Today), unreadable, except: candidate.Id);

        var result = ActivityCheck.Check(plannedCandidate, others, user.Context);
        return unreadable.Count == 0 ? result : result with { Notes = [.. unreadable, .. result.Notes] };
    }
}
