using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Domain.Templates;
using FitnessTracker.Domain.Time;
using FitnessTracker.Domain.Workouts;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.TimeZones;

namespace FitnessTracker.Web.Services;

/// <summary>
/// What the planning pages need about the signed-in user, read once through the per-user filter. <see cref="Now"/> is
/// the moment it was read, in the user's timezone; the context's "today" is its date. <see cref="Routines"/> are those
/// the context's routine calendar and bases were made from, for callers that need them too.
/// </summary>
public sealed record UserPlanning(
    UserPreferences Preferences,
    bool PreferencesSaved,
    IReadOnlyList<Location> Locations,
    IReadOnlyList<TravelLeg> Legs,
    PlanningContext Context,
    ZonedDateTime Now,
    UserRoutines Routines)
{
    /// <summary>The week <paramref name="date"/> is in, by the week start in Preferences (ACC-3).</summary>
    public DateInterval WeekOf(LocalDate date)
    {
        var first = CalendarWeeks.StartOf(date, Preferences.WeekStart);
        return new DateInterval(first, first.PlusDays(6));
    }
}

/// <summary>
/// FIT-5: one of a goal's sessions (<see cref="GoalSessions"/>) with its state: <see cref="HasStatuses"/> when it is an
/// exercise session (Q20), and then its <see cref="State"/> (FIT-1), which only a session not planned at all lacks.
/// </summary>
public sealed record GoalSessionState(GoalSession Session, bool HasStatuses, SessionState? State);

/// <summary>
/// FIT-5, Q37: the user's weekly goals, highest priority first then by name (as the planner fits them in, §7.4), and
/// each one's progress in <see cref="Week"/>, by goal; <see cref="Unreadable"/> names any activity left out (P1).
/// </summary>
public sealed record GoalWeeks(DateInterval Week, IReadOnlyList<WeeklyGoal> Goals, IReadOnlyDictionary<Guid, GoalProgress> Progress, IReadOnlyList<string> Unreadable);

/// <summary>
/// CAL-9: the signed-in user's routines, which one is in use on each date, and the dates chosen for them (Q14),
/// earliest first.
/// </summary>
public sealed record UserRoutines(IReadOnlyList<Routine> All, RoutineCalendar Calendar, IReadOnlyList<RoutineDateRange> Dates);

/// <summary>
/// Reads the signed-in user's preferences (or the defaults), locations, travel times and "today" in their timezone,
/// the same way for the check on save and the home page, so the two always agree.
/// </summary>
public class PlanningData(AppDbContext db, IClock clock)
{
    public async Task<UserPlanning> LoadAsync()
    {
        var saved = await db.UserPreferences.AsNoTracking().SingleOrDefaultAsync();
        var preferences = saved ?? new UserPreferences();
        var locations = await db.Locations.AsNoTracking().Include(location => location.OpeningTimes).ToListAsync();
        var legs = await db.TravelLegs.AsNoTracking().ToListAsync();

        var now = clock.GetCurrentInstant().InZone(DateTimeZoneProviders.Tzdb[preferences.TimeZoneId]);
        var routines = await RoutinesAsync();
        var context = new PlanningContext(
            now.Date,
            locations.FirstOrDefault(location => location.IsHome)?.Id,
            TravelLeg.ToTravelTimes(legs),
            locations.ToDictionary(location => location.Id, location => new Place(location.Name, location.ToOpeningHours())),
            preferences.TimesShownAs())
        {
            Rest = RestRulesOf(preferences),
            // TRV-4: travel starts from the base of the routine in use each day, if it has one.
            Routines = routines.Calendar,
            RoutineBases = routines.All
                .Where(routine => routine.BaseLocationId is not null)
                .ToDictionary(routine => routine.Id, routine => routine.BaseLocationId!.Value),
        };
        return new UserPlanning(preferences, saved is not null, locations, legs, context, now, routines);
    }

    /// <summary>ACC-7: H5-H7 from <paramref name="preferences"/>, for the check on save and the planner alike.</summary>
    public static RestRules RestRulesOf(UserPreferences preferences) => new(
        Duration.FromHours(preferences.MinimumHoursBetweenDemandingSessions),
        preferences.MaximumDemandingSessionsPerDay,
        Week.DaysIn(preferences.RecoveryDays).ToHashSet());

    /// <summary>ACC-6: each category's default getting-ready and changing minutes: the user's own, or the built-in ones.</summary>
    public async Task<IReadOnlyDictionary<ActivityCategory, (int Preparation, int Changing)>> DefaultBuffersAsync()
    {
        var stored = await db.CategoryBuffers.AsNoTracking().ToListAsync();
        return Enum.GetValues<ActivityCategory>().ToDictionary(
            category => category,
            category => stored.FirstOrDefault(row => row.Category == category) is { } saved
                ? (saved.PreparationMinutes, saved.ChangingMinutes)
                : CategoryBuffers.BuiltIn(category));
    }

    /// <summary>
    /// CAL-9: the user's routines, the dates chosen for them (Q14), and which is in use on each date. A routine whose
    /// cycle can't be read, or dates whose last is before their first (edited by hand), are left out, rather than
    /// stopping every page.
    /// </summary>
    public async Task<UserRoutines> RoutinesAsync()
    {
        var all = await db.Routines.AsNoTracking().ToListAsync();
        var dates = (await db.RoutineDateRanges.AsNoTracking().ToListAsync())
            .Where(chosen => chosen.StartDate <= chosen.EndDate)
            .OrderBy(chosen => chosen.StartDate)
            .ThenBy(chosen => chosen.EndDate)
            .ToList();
        var cycles = new Dictionary<Guid, WeekCycle>();
        foreach (var routine in all)
        {
            try
            {
                cycles[routine.Id] = routine.ToCycle();
            }
            catch (ArgumentOutOfRangeException)
            {
                // Left out: see above.
            }
        }

        var usual = all.FirstOrDefault(routine => routine.IsUsual)?.Id;
        return new UserRoutines(all, new RoutineCalendar(cycles, usual, dates.Select(chosen => chosen.ToRoutineDates())), dates);
    }

    /// <summary>
    /// CAL-9: the user's activities, as stored, that a change to which routine is in use when (dates chosen for one,
    /// another made usual) can move: those in a routine, whose occurrences change. With <paramref name="atAPlaceToo"/>
    /// (a routine has a base of its own, TRV-4), also every other one at a place still to happen from
    /// <paramref name="today"/>, whose travel then starts from the base of the routine in use. One at no particular place
    /// in every week is never affected.
    /// </summary>
    public Task<List<Activity>> ActivitiesARoutineChangeCanMoveAsync(bool atAPlaceToo, LocalDate today) =>
        db.Activities.AsNoTracking()
            .Where(activity => activity.RoutineId != null
                || (atAPlaceToo && activity.LocationId != null && (activity.RepeatRule != null || activity.StartDate >= today)))
            .ToListAsync();

    /// <summary>
    /// The user's activities that can fall on <paramref name="dates"/>: repeating ones that have started by then, and
    /// one-offs on those dates, each in the weeks <paramref name="routines"/> allows it (CAL-9: those loaded with the
    /// user, <see cref="UserPlanning.Routines"/>). A repeating one with occurrences changed on their own (CAL-4) is
    /// loaded whatever its first date, as one can have been moved to before it. One the app can't read (edited by hand,
    /// say) is left out and named in <paramref name="unreadable"/>, rather than stopping the page.
    /// </summary>
    public async Task<List<PlannedActivity>> ActivitiesOnAsync(DateInterval dates, RoutineCalendar routines, List<string> unreadable, Guid? except = null)
    {
        var stored = await db.Activities.AsNoTracking()
            .Where(activity => activity.Id != except
                && (activity.OccurrenceChanges != null
                    || (activity.StartDate <= dates.End && (activity.RepeatRule != null || activity.StartDate >= dates.Start))))
            .ToListAsync();
        return Planned(stored, routines, unreadable);
    }

    /// <summary>
    /// FIT-1: the state at <paramref name="now"/> of each exercise session (Q20) among <paramref name="items"/>, from what
    /// each was marked as, by its activity and the date its rule puts it on (so a moved one keeps its mark). The same
    /// for every page that lists sessions. Others have none.
    /// </summary>
    public async Task<IReadOnlyDictionary<(Guid Activity, LocalDate Date), SessionState>> SessionStatesAsync(IReadOnlyList<AgendaItem> items, Instant now)
    {
        var sessions = items.Select(item => (item.Activity, item.Occurrence)).ToList();
        return await StatesAsync(sessions, await ExerciseAmongAsync(sessions), now);
    }

    /// <summary>
    /// FIT-5, Q38: the sessions of the user's goals that start on <paramref name="dates"/> in their timezone, as the
    /// planner counts them (<see cref="GoalSessions.On"/>), each with its state at <paramref name="now"/>. Marks are read
    /// by each session's rule date, so a moved one keeps its own (CAL-4).
    /// </summary>
    public async Task<IReadOnlyList<GoalSessionState>> GoalSessionsAsync(IEnumerable<PlannedActivity> activities, DateInterval dates, ZonedDateTime now)
    {
        var sessions = GoalSessions.On(activities, dates, now.Zone);
        var occurrences = sessions.Select(session => (session.Activity, session.Occurrence)).ToList();
        var exercise = await ExerciseAmongAsync(occurrences);
        var states = await StatesAsync(occurrences, exercise, now.ToInstant());
        return sessions
            .Select(session => new GoalSessionState(
                session,
                exercise.Contains(session.Activity.Id),
                states.TryGetValue((session.Activity.Id, session.Occurrence.Date), out var state) ? state : null))
            .ToList();
    }

    /// <summary>FIT-5, Q24, Q37: each of the user's weekly goals and its progress in <paramref name="week"/>.</summary>
    public async Task<GoalWeeks> GoalWeeksAsync(UserPlanning user, DateInterval week)
    {
        var unreadable = new List<string>();
        // GoalSessions.On looks two days either side, for activities in another timezone.
        var activities = await ActivitiesOnAsync(new DateInterval(week.Start.PlusDays(-2), week.End.PlusDays(2)), user.Routines.Calendar, unreadable);
        var goals = await db.WeeklyGoals.AsNoTracking().ToListAsync();
        var sessions = await GoalSessionsAsync(activities, week, user.Now);
        // Sorted here rather than by SQLite, which puts every capital letter before every lower-case one.
        return new GoalWeeks(
            week,
            goals.OrderByDescending(goal => goal.Priority).ThenBy(goal => goal.Title, StringComparer.InvariantCultureIgnoreCase).ToList(),
            ProgressOf(goals, sessions),
            unreadable);
    }

    /// <summary>
    /// FIT-5: each goal's progress from its sessions (<see cref="GoalProgress.Of"/>): the states of those with statuses,
    /// and how many without (Q20).
    /// </summary>
    public static IReadOnlyDictionary<Guid, GoalProgress> ProgressOf(IEnumerable<WeeklyGoal> goals, IEnumerable<GoalSessionState> sessions)
    {
        var byGoal = sessions.ToLookup(session => session.Session.Goal);
        return goals.ToDictionary(
            goal => goal.Id,
            goal => GoalProgress.Of(
                goal.SessionsPerWeek,
                byGoal[goal.Id].Where(session => session.HasStatuses).Select(session => session.State),
                withoutStatuses: byGoal[goal.Id].Count(session => !session.HasStatuses)));
    }

    // Q20: which of these sessions' activities are in an exercise category, so have statuses.
    private async Task<HashSet<Guid>> ExerciseAmongAsync(IReadOnlyList<(PlannedActivity Activity, Occurrence Occurrence)> sessions)
    {
        var ids = sessions.Select(session => session.Activity.Id).Distinct().ToList();
        return (await db.Activities.Where(activity => ids.Contains(activity.Id)).Select(activity => new { activity.Id, activity.Category }).ToListAsync())
            .Where(activity => activity.Category.IsExercise())
            .Select(activity => activity.Id)
            .ToHashSet();
    }

    // FIT-1: the state of each session of an exercise activity among these, from its mark, read by the date its rule
    // puts it on.
    private async Task<Dictionary<(Guid Activity, LocalDate Date), SessionState>> StatesAsync(
        IReadOnlyList<(PlannedActivity Activity, Occurrence Occurrence)> occurrences, HashSet<Guid> exercise, Instant now)
    {
        var sessions = occurrences.Where(session => exercise.Contains(session.Activity.Id)).ToList();
        var states = new Dictionary<(Guid Activity, LocalDate Date), SessionState>();
        if (sessions.Count == 0)
        {
            return states;
        }

        var (first, last) = (sessions.Min(session => session.Occurrence.Date), sessions.Max(session => session.Occurrence.Date));
        var marks = await db.SessionLogs
            .Where(log => exercise.Contains(log.ActivityId) && log.OccurrenceDate >= first && log.OccurrenceDate <= last)
            .ToDictionaryAsync(log => (log.ActivityId, log.OccurrenceDate), log => log.Mark);
        foreach (var (activity, occurrence) in sessions)
        {
            var key = (activity.Id, occurrence.Date);
            if (Sessions.StateOf(activity.Series, occurrence.Date, marks.TryGetValue(key, out var mark) ? mark : null, now) is { } state)
            {
                states[key] = state;
            }
        }

        return states;
    }

    /// <summary>
    /// P1: every stored activity the app can't read, named, for a page that shows only some dates (the calendar), so
    /// one isn't just missing.
    /// </summary>
    public async Task<List<string>> UnreadableAsync()
    {
        var unreadable = new List<string>();
        Planned(await db.Activities.AsNoTracking().ToListAsync(), RoutineCalendar.None, unreadable);
        return unreadable;
    }

    private static List<PlannedActivity> Planned(List<Activity> stored, RoutineCalendar routines, List<string> unreadable)
    {
        var planned = new List<PlannedActivity>();
        foreach (var activity in stored)
        {
            try
            {
                planned.Add(activity.ToPlannedActivity(routines));
            }
            catch (Exception error) when (error is FormatException or DateTimeZoneNotFoundException)
            {
                unreadable.Add($"{activity.Title} couldn't be read, so it's left out. Edit or delete it to fix this.");
            }
        }

        return planned;
    }
}
