using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Domain.Templates;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.TimeZones;

namespace FitnessTracker.Web.Services;

/// <summary>
/// What the planning pages need about the signed-in user, read once through the per-user filter. <see cref="Now"/> is
/// the moment it was read, in the user's timezone; the context's "today" is its date.
/// </summary>
public sealed record UserPlanning(
    UserPreferences Preferences,
    bool PreferencesSaved,
    IReadOnlyList<Location> Locations,
    IReadOnlyList<TravelLeg> Legs,
    PlanningContext Context,
    ZonedDateTime Now);

/// <summary>CAL-9: the signed-in user's routines, and which one is in use on each date.</summary>
public sealed record UserRoutines(IReadOnlyList<Routine> All, RoutineCalendar Calendar);

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
        var context = new PlanningContext(
            now.Date,
            locations.FirstOrDefault(location => location.IsHome)?.Id,
            TravelLeg.ToTravelTimes(legs),
            locations.ToDictionary(location => location.Id, location => new Place(location.Name, location.ToOpeningHours())),
            preferences.TimesShownAs())
        {
            Rest = RestRulesOf(preferences),
        };
        return new UserPlanning(preferences, saved is not null, locations, legs, context, now);
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
    /// CAL-9: the user's routines, and which is in use on each date. A routine whose cycle can't be read (edited by
    /// hand) is left out, so its activities don't happen, rather than stopping every page.
    /// </summary>
    public async Task<UserRoutines> RoutinesAsync()
    {
        var all = await db.Routines.AsNoTracking().ToListAsync();
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
        return new UserRoutines(all, new RoutineCalendar(cycles, usual, []));
    }

    /// <summary>
    /// The user's activities that can fall on <paramref name="dates"/>: repeating ones that have started by then, and
    /// one-offs on those dates, each in the weeks its routine allows (CAL-9). A repeating one with occurrences changed
    /// on their own (CAL-4) is loaded whatever its first date, as one can have been moved to before it. One the app
    /// can't read (edited by hand, say) is left out and named in <paramref name="unreadable"/>, rather than stopping
    /// the page.
    /// </summary>
    public async Task<List<PlannedActivity>> ActivitiesOnAsync(DateInterval dates, List<string> unreadable, Guid? except = null)
    {
        var stored = await db.Activities.AsNoTracking()
            .Where(activity => activity.Id != except
                && (activity.OccurrenceChanges != null
                    || (activity.StartDate <= dates.End && (activity.RepeatRule != null || activity.StartDate >= dates.Start))))
            .ToListAsync();
        return Planned(stored, (await RoutinesAsync()).Calendar, unreadable);
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
