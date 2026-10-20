using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Web.Data;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.TimeZones;

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

        var zone = DateTimeZoneProviders.Tzdb[preferences.TimeZoneId];
        var today = clock.GetCurrentInstant().InZone(zone).Date;

        // Only activities that can fall near the dates checked: repeating ones that start in time, and one-offs on
        // those dates. A year of past one-offs isn't read on every save.
        var nearby = ActivityCheck.OthersThatCanClash(candidate.StartDate, today);
        var others = await db.Activities.AsNoTracking()
            .Where(activity => activity.Id != candidate.Id && activity.StartDate <= nearby.End
                && (activity.RepeatRule != null || activity.StartDate >= nearby.Start))
            .ToListAsync();

        // One stored activity the app can't read (edited by hand, say) is left out with a note, not allowed to stop
        // every save.
        var planned = new List<PlannedActivity>();
        var unreadable = new List<string>();
        foreach (var other in others)
        {
            try
            {
                planned.Add(other.ToPlannedActivity());
            }
            catch (Exception error) when (error is FormatException or DateTimeZoneNotFoundException)
            {
                unreadable.Add($"{other.Title} couldn't be read, so it wasn't checked against. Edit or delete it to fix this.");
            }
        }

        var context = new PlanningContext(
            today,
            locations.FirstOrDefault(location => location.IsHome)?.Id,
            TravelLeg.ToTravelTimes(legs),
            locations.ToDictionary(location => location.Id, location => new Place(location.Name, location.ToOpeningHours())));
        var result = ActivityCheck.Check(candidate.ToPlannedActivity(), planned, context);
        return unreadable.Count == 0 ? result : result with { Notes = [.. unreadable, .. result.Notes] };
    }
}
