using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Web.Data;

namespace FitnessTracker.Web.Services;

/// <summary>
/// SCH-1: runs the one check (<see cref="ActivityCheck"/>) on an activity about to be saved, against the signed-in
/// user's other activities, locations and travel times (<see cref="PlanningData"/>). Every way of saving an activity
/// calls this; the rules themselves are in the domain.
/// </summary>
public class ActivityChecker(PlanningData planning)
{
    public async Task<ActivityCheckResult> CheckAsync(Activity candidate)
    {
        var user = await planning.LoadAsync();

        // Only activities that can fall near the dates checked, not every one the user ever had; one that can't be read
        // is left out with a note, not allowed to stop every save.
        var plannedCandidate = candidate.ToPlannedActivity();
        var unreadable = new List<string>();
        var others = await planning.ActivitiesOnAsync(ActivityCheck.OthersThatCanClash(plannedCandidate.Series, user.Context.Today), unreadable, except: candidate.Id);

        var result = ActivityCheck.Check(plannedCandidate, others, user.Context);
        return unreadable.Count == 0 ? result : result with { Notes = [.. unreadable, .. result.Notes] };
    }
}
