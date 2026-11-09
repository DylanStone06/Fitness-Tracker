using FitnessTracker.Domain.Workouts;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models.Activities;

namespace FitnessTracker.Web.Models.Goals;

/// <summary>
/// FIT-5, Q37: a goal's week as a page shows it: <see cref="Label"/> (the goal's name, or "This week" where the name is
/// already shown) and <see cref="Headline"/> ("1 of 3 done"), then a bar of <see cref="Value"/> out of
/// <see cref="Max"/> named <see cref="BarName"/> for a screen reader, then <see cref="Details"/> ("1 skipped · 1 to
/// come"), if any. The words say everything the bar does (design rule 1).
/// </summary>
public sealed record GoalWeekView(string Title, string Label, string Headline, string? Details, int Value, int Max)
{
    public string BarName => $"{Title}: {Headline}";
}

/// <summary>GOL-1: a weekly goal in words, as the list and the planner write it.</summary>
public static class GoalDisplay
{
    /// <summary>
    /// FIT-5, Q24: the goal's week. An exercise goal counts what's done (its sessions have statuses, Q20), "goal met"
    /// once it has as many as it asks for; any other goal counts what's planned. Then what else there is: skipped, not
    /// logged, to come, planned without statuses, and not planned yet. Never a failure (P3, design rule 6).
    /// </summary>
    public static GoalWeekView WeekOf(WeeklyGoal goal, GoalProgress progress, string? label = null)
    {
        var withStatuses = goal.Category.IsExercise();
        var counted = withStatuses ? progress.Done : progress.Done + progress.NotLogged + progress.ToCome + progress.Planned;
        var headline = $"{counted} of {progress.Target} {(withStatuses ? "done" : "planned")}"
            + (withStatuses && counted >= progress.Target ? ": goal met" : "");
        var details = new List<string>();
        Add(progress.Skipped, "skipped");
        if (withStatuses)
        {
            Add(progress.NotLogged, "not logged");
            Add(progress.ToCome, "to come");
            Add(progress.Planned, "planned");
        }

        Add(progress.NotPlanned, "not planned yet");
        return new GoalWeekView(goal.Title, label ?? goal.Title, headline, details.Count == 0 ? null : string.Join(" · ", details),
            Math.Min(counted, progress.Target), progress.Target);

        void Add(int count, string what)
        {
            if (count > 0)
            {
                details.Add($"{count} {what}");
            }
        }
    }

    /// <summary>"Once a week, 50 min each", "Twice a week, 1 h each" or "3 times a week, 1 h 30 min each".</summary>
    public static string HowOften(WeeklyGoal goal)
    {
        var times = goal.SessionsPerWeek switch
        {
            1 => "Once",
            2 => "Twice",
            var count => $"{count} times",
        };
        return $"{times} a week, {ActivityDisplay.Length(goal.SessionMinutes)} each";
    }

    /// <summary>"Preferably Mon, Thu, in the evening", "Preferably in the morning", or null for any day at any time.</summary>
    public static string? Preferred(WeeklyGoal goal)
    {
        var days = string.Join(", ", Week.DaysIn(goal.PreferredDays).Select(day => day.ToString()[..3]));
        var time = goal.PreferredTimeOfDay switch
        {
            PreferredTimeOfDay.Morning => "in the morning",
            PreferredTimeOfDay.Midday => "around midday",
            PreferredTimeOfDay.Afternoon => "in the afternoon",
            PreferredTimeOfDay.Evening => "in the evening",
            _ => null,
        };
        var parts = new[] { days, time }.Where(part => !string.IsNullOrEmpty(part)).ToList();
        return parts.Count == 0 ? null : $"Preferably {string.Join(", ", parts)}";
    }
}
