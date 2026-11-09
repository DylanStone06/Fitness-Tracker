using FitnessTracker.Domain.Footprints;
using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Domain.Workouts;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace FitnessTracker.Web.Services;

/// <summary>
/// SCH-3: a week planned for the signed-in user, with what the page needs to show it: each goal's progress that week, by
/// goal (FIT-5, counted as the planner counts), and its routines (CAL-9: the week's routine and week, and the weeks a
/// session is added to).
/// </summary>
public sealed record PlannedWeek(
    DateInterval Week,
    WeekPlan Plan,
    IReadOnlyDictionary<Guid, WeeklyGoal> Goals,
    IReadOnlyDictionary<Guid, GoalProgress> Progress,
    UserPlanning User,
    IReadOnlyList<string> Unreadable,
    UserRoutines Routines);

/// <summary>SCH-6: other times for one occurrence in its week, with what the page needs to show them.</summary>
public sealed record OtherTimes(Occurrence Occurrence, DateInterval Week, SessionAlternatives Options, UserPlanning User, IReadOnlyList<string> Unreadable);

/// <summary>
/// SCH-3: plans a week of the signed-in user's weekly goals (GOL-1) with the domain's <see cref="Planner"/>, around
/// their activities, sleep and rest rules from Preferences (ACC-7), the same way every time (SCH-7).
/// </summary>
public class WeekPlanning(AppDbContext db, PlanningData planning)
{
    /// <summary>The week (by the week start in Preferences) that <paramref name="date"/> is in, or this week.</summary>
    public async Task<PlannedWeek> PlanAsync(LocalDate? date, IReadOnlyCollection<RejectedDay> rejected)
    {
        var user = await planning.LoadAsync();
        var preferences = user.Preferences;
        var week = user.WeekOf(date ?? user.Now.Date);
        var unreadable = new List<string>();
        var rest = user.Context.Rest;
        var loaded = Planner.DatesToLoadFor(week, rest.BetweenDemanding);
        var activities = await planning.ActivitiesOnAsync(loaded, user.Routines.Calendar, unreadable);
        var goals = await db.WeeklyGoals.AsNoTracking().ToListAsync();
        var request = new PlanRequest(
            week,
            user.Now.ToInstant(),
            user.Now.Zone,
            activities,
            goals.Select(goal => ToPlanGoal(goal, preferences)).ToList(),
            Sleep(preferences),
            rest,
            user.Context)
        {
            Rejected = rejected,
            // Q38: a session marked skipped doesn't count towards its goal, so another time is found for it.
            Skipped = await planning.SkippedAsync(activities, loaded, user.Now.ToInstant()),
        };
        // FIT-5, Q37: each goal's week, from the same activities, so it agrees with what the planner counts.
        var progress = PlanningData.ProgressOf(goals, await planning.GoalSessionsAsync(activities, week, user.Now));
        return new PlannedWeek(week, Planner.Plan(request), goals.ToDictionary(goal => goal.Id), progress, user, unreadable, user.Routines);
    }

    /// <summary>
    /// SCH-6, T16: up to <paramref name="most"/> other times, on different days later in its week, for the occurrence of
    /// <paramref name="activity"/> its rule puts on <paramref name="date"/>, around everything else, which stays where
    /// it is. The time it has now isn't offered; the part of the day it's in is preferred. Null when it doesn't happen
    /// that day.
    /// </summary>
    public async Task<OtherTimes?> OptionsAsync(Activity activity, LocalDate date, int most)
    {
        var user = await planning.LoadAsync();
        var session = activity.ToPlannedActivity(user.Routines.Calendar);
        if (ActivityCheck.OccurrenceOn(session.Series, date) is not { } occurrence)
        {
            return null;
        }

        var zone = user.Now.Zone;
        var week = user.WeekOf(occurrence.Time.Start.InZone(zone).Date);
        var unreadable = new List<string>();
        var loaded = Planner.DatesToLoadFor(week, user.Context.Rest.BetweenDemanding);
        var activities = await planning.ActivitiesOnAsync(loaded, user.Routines.Calendar, unreadable);
        var request = new PlanRequest(week, user.Now.ToInstant(), zone, activities, [], Sleep(user.Preferences), user.Context.Rest, user.Context)
        {
            // Q38 (scheduling review of Q38, L2): a day whose only session of it is skipped can be offered.
            Skipped = await planning.SkippedAsync(activities, loaded, user.Now.ToInstant()),
        };
        // Not null: the occurrence was found above.
        var options = Planner.OptionsFor(request, session, date, (int)activity.Priority, most)!;
        return new OtherTimes(occurrence, week, options, user, unreadable);
    }

    private static SleepWindow Sleep(UserPreferences preferences) => new(
        preferences.UsualBedtime,
        preferences.UsualWakeTime,
        Duration.FromMinutes(preferences.WindDownMinutes),
        Duration.FromMinutes(preferences.WakeUpRoutineMinutes));

    // ACC-7: an exercise goal with no preferred days or time of its own takes the ones in Preferences.
    private static PlanGoal ToPlanGoal(WeeklyGoal goal, UserPreferences preferences)
    {
        var exercise = goal.Category.IsExercise();
        var days = goal.PreferredDays == DaysOfWeek.None && exercise ? preferences.PreferredExerciseDays : goal.PreferredDays;
        var time = goal.PreferredTimeOfDay == PreferredTimeOfDay.Any && exercise ? preferences.PreferredTimeOfDay : goal.PreferredTimeOfDay;
        return new PlanGoal(
            goal.Id,
            goal.Title,
            goal.SessionsPerWeek,
            Duration.FromMinutes(goal.SessionMinutes),
            new Buffers(Duration.FromMinutes(goal.PreparationMinutes), Duration.FromMinutes(goal.ChangingMinutes)),
            goal.LocationId,
            goal.IsDemanding,
            (int)goal.Priority,
            Week.DaysIn(days).ToHashSet(),
            time switch
            {
                PreferredTimeOfDay.Morning => TimeOfDay.Morning,
                PreferredTimeOfDay.Midday => TimeOfDay.Midday,
                PreferredTimeOfDay.Afternoon => TimeOfDay.Afternoon,
                PreferredTimeOfDay.Evening => TimeOfDay.Evening,
                _ => TimeOfDay.Any,
            });
    }
}
