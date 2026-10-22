using FitnessTracker.Domain.Footprints;
using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Domain.Time;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace FitnessTracker.Web.Services;

/// <summary>SCH-3: a week planned for the signed-in user, with what the page needs to show it.</summary>
public sealed record PlannedWeek(
    DateInterval Week,
    WeekPlan Plan,
    IReadOnlyDictionary<Guid, WeeklyGoal> Goals,
    UserPlanning User,
    IReadOnlyList<string> Unreadable);

/// <summary>SCH-6: other times for one occurrence in its week, with what the page needs to show them.</summary>
public sealed record OtherTimes(Occurrence Occurrence, DateInterval Week, SessionAlternatives Options, UserPlanning User, IReadOnlyList<string> Unreadable);

/// <summary>
/// SCH-3: plans a week of the signed-in user's weekly goals (GOL-1) with the domain's <see cref="Planner"/>, around
/// their activities, sleep and rest rules from Preferences (ACC-7), the same way every time (SCH-7).
/// </summary>
public class WeekPlanning(AppDbContext db, PlanningData planning)
{
    private static readonly ActivityCategory[] Exercise = [ActivityCategory.Gym, ActivityCategory.Run, ActivityCategory.OtherExercise];

    /// <summary>The week (by the week start in Preferences) that <paramref name="date"/> is in, or this week.</summary>
    public async Task<PlannedWeek> PlanAsync(LocalDate? date, IReadOnlyCollection<RejectedDay> rejected)
    {
        var user = await planning.LoadAsync();
        var preferences = user.Preferences;
        var first = CalendarWeeks.StartOf(date ?? user.Now.Date, preferences.WeekStart);
        var week = new DateInterval(first, first.PlusDays(6));
        var unreadable = new List<string>();
        var rest = user.Context.Rest;
        var activities = await planning.ActivitiesOnAsync(Planner.DatesToLoadFor(week, rest.BetweenDemanding), unreadable);
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
        };
        return new PlannedWeek(week, Planner.Plan(request), goals.ToDictionary(goal => goal.Id), user, unreadable);
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
        var session = activity.ToPlannedActivity();
        if (ActivityCheck.OccurrenceOn(session.Series, date) is not { } occurrence)
        {
            return null;
        }

        var zone = user.Now.Zone;
        var start = occurrence.Time.Start.InZone(zone);
        var first = CalendarWeeks.StartOf(start.Date, user.Preferences.WeekStart);
        var week = new DateInterval(first, first.PlusDays(6));
        var unreadable = new List<string>();
        var activities = (await planning.ActivitiesOnAsync(Planner.DatesToLoadFor(week, user.Context.Rest.BetweenDemanding), unreadable))
            .Select(other => other.Id == activity.Id ? other with { Series = other.Series.CancelOccurrence(date) } : other)
            .ToList();
        var asGoal = new PlanGoal(
            activity.Id,
            activity.Title,
            1,
            occurrence.Time.Duration,
            session.Buffers,
            activity.LocationId,
            activity.IsDemanding,
            (int)activity.Priority,
            new HashSet<IsoDayOfWeek>(),
            new[] { TimeOfDay.Morning, TimeOfDay.Midday, TimeOfDay.Afternoon, TimeOfDay.Evening }
                .FirstOrDefault(part => Planner.HoursOf(part) is { } hours && start.TimeOfDay >= hours.From && start.TimeOfDay < hours.To));
        var request = new PlanRequest(week, user.Now.ToInstant(), zone, activities, [asGoal], Sleep(user.Preferences), user.Context.Rest, user.Context);
        return new OtherTimes(occurrence, week, Planner.Options(request, asGoal, most, notAt: occurrence.Time.Start), user, unreadable);
    }

    private static SleepWindow Sleep(UserPreferences preferences) => new(
        preferences.UsualBedtime,
        preferences.UsualWakeTime,
        Duration.FromMinutes(preferences.WindDownMinutes),
        Duration.FromMinutes(preferences.WakeUpRoutineMinutes));

    // ACC-7: an exercise goal with no preferred days or time of its own takes the ones in Preferences.
    private static PlanGoal ToPlanGoal(WeeklyGoal goal, UserPreferences preferences)
    {
        var exercise = Exercise.Contains(goal.Category);
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
