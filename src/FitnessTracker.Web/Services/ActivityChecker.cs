using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Web.Data;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace FitnessTracker.Web.Services;

/// <summary>
/// SCH-1: runs the one check (<see cref="ActivityCheck"/>) on an activity about to be saved, against the signed-in
/// user's other activities, locations and travel times. Everything is read through the per-user filter, and "today"
/// is in the user's timezone. Every way of saving an activity calls this; the rules themselves are in the domain.
/// </summary>
public class ActivityChecker(AppDbContext db, IClock clock)
{
    public async Task<ActivityCheckResult> CheckAsync(Activity candidate)
    {
        var preferences = await db.UserPreferences.AsNoTracking().SingleOrDefaultAsync() ?? new UserPreferences();
        var locations = await db.Locations.AsNoTracking().Include(location => location.OpeningTimes).ToListAsync();
        var legs = await db.TravelLegs.AsNoTracking().ToListAsync();
        var others = await db.Activities.AsNoTracking().Where(activity => activity.Id != candidate.Id).ToListAsync();

        var zone = DateTimeZoneProviders.Tzdb[preferences.TimeZoneId];
        var context = new PlanningContext(
            clock.GetCurrentInstant().InZone(zone).Date,
            locations.FirstOrDefault(location => location.IsHome)?.Id,
            TravelLeg.ToTravelTimes(legs),
            locations.ToDictionary(location => location.Id, location => new Place(location.Name, location.ToOpeningHours())));
        return ActivityCheck.Check(candidate.ToPlannedActivity(), others.Select(other => other.ToPlannedActivity()), context);
    }
}
