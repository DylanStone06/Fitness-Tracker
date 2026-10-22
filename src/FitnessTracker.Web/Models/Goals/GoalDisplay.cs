using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models.Activities;

namespace FitnessTracker.Web.Models.Goals;

/// <summary>GOL-1: a weekly goal in words, as the list and the planner write it.</summary>
public static class GoalDisplay
{
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
