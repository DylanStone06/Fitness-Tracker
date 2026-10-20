using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Web.Data;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.TimeZones;

namespace FitnessTracker.Web.Services;

/// <summary>What the planning pages need about the signed-in user, read once through the per-user filter.</summary>
public sealed record UserPlanning(
    UserPreferences Preferences,
    bool PreferencesSaved,
    IReadOnlyList<Location> Locations,
    IReadOnlyList<TravelLeg> Legs,
    PlanningContext Context);

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

        var zone = DateTimeZoneProviders.Tzdb[preferences.TimeZoneId];
        var context = new PlanningContext(
            clock.GetCurrentInstant().InZone(zone).Date,
            locations.FirstOrDefault(location => location.IsHome)?.Id,
            TravelLeg.ToTravelTimes(legs),
            locations.ToDictionary(location => location.Id, location => new Place(location.Name, location.ToOpeningHours())));
        return new UserPlanning(preferences, saved is not null, locations, legs, context);
    }

    /// <summary>
    /// The user's activities that can fall on <paramref name="dates"/>: repeating ones that have started by then, and
    /// one-offs on those dates. One the app can't read (edited by hand, say) is left out and named in
    /// <paramref name="unreadable"/>, rather than stopping the page.
    /// </summary>
    public async Task<List<PlannedActivity>> ActivitiesOnAsync(DateInterval dates, List<string> unreadable, Guid? except = null)
    {
        var stored = await db.Activities.AsNoTracking()
            .Where(activity => activity.Id != except && activity.StartDate <= dates.End
                && (activity.RepeatRule != null || activity.StartDate >= dates.Start))
            .ToListAsync();

        var planned = new List<PlannedActivity>();
        foreach (var activity in stored)
        {
            try
            {
                planned.Add(activity.ToPlannedActivity());
            }
            catch (Exception error) when (error is FormatException or DateTimeZoneNotFoundException)
            {
                unreadable.Add($"{activity.Title} couldn't be read, so it wasn't checked against. Edit or delete it to fix this.");
            }
        }

        return planned;
    }
}
