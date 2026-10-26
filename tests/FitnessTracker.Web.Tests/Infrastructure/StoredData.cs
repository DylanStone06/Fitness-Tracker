using FitnessTracker.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FitnessTracker.Web.Tests.Infrastructure;

/// <summary>
/// Reads what is really stored, past the per-user filter, so a test checks the database itself rather than
/// what a page chose to show.
/// </summary>
public static class StoredData
{
    public static Task<Location?> LocationNamedAsync(TestAppFactory factory, string name) =>
        ReadAsync(factory, db => db.Locations.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(location => location.Name == name));

    public static Task<int> LocationCountAsync(TestAppFactory factory) =>
        ReadAsync(factory, db => db.Locations.IgnoreQueryFilters().CountAsync());

    public static Task<TravelLeg?> TravelLegAsync(TestAppFactory factory, Guid from, Guid to) =>
        ReadAsync(factory, db => db.TravelLegs.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(leg => leg.FromLocationId == from && leg.ToLocationId == to));

    public static Task<int> TravelLegCountAsync(TestAppFactory factory) =>
        ReadAsync(factory, db => db.TravelLegs.IgnoreQueryFilters().CountAsync());

    public static Task<Activity?> ActivityTitledAsync(TestAppFactory factory, string title) =>
        ReadAsync(factory, db => db.Activities.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(activity => activity.Title == title));

    public static Task<int> ActivityCountAsync(TestAppFactory factory) =>
        ReadAsync(factory, db => db.Activities.IgnoreQueryFilters().CountAsync());

    public static Task<WeeklyGoal?> GoalTitledAsync(TestAppFactory factory, string title) =>
        ReadAsync(factory, db => db.WeeklyGoals.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(goal => goal.Title == title));

    public static Task<Routine?> RoutineNamedAsync(TestAppFactory factory, string name) =>
        ReadAsync(factory, db => db.Routines.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(routine => routine.Name == name));

    /// <summary>CAL-9: the dates chosen for this routine, earliest first.</summary>
    public static Task<List<RoutineDateRange>> DatesChosenForAsync(TestAppFactory factory, Guid routine) =>
        ReadAsync(factory, async db => (await db.RoutineDateRanges.IgnoreQueryFilters().AsNoTracking()
                .Where(dates => dates.RoutineId == routine).ToListAsync())
            .OrderBy(dates => dates.StartDate).ToList());

    /// <summary>The stored version of any record with this id (a location, travel leg, activity, weekly goal, routine or
    /// dates chosen for one), or null if there is none. Every save changes it, so an unchanged version means the record
    /// was left alone.</summary>
    public static Task<Guid?> VersionOfAsync(TestAppFactory factory, Guid id) =>
        ReadAsync(factory, async db =>
            await db.Locations.IgnoreQueryFilters().Where(location => location.Id == id).Select(location => (Guid?)location.Version).SingleOrDefaultAsync()
            ?? await db.TravelLegs.IgnoreQueryFilters().Where(leg => leg.Id == id).Select(leg => (Guid?)leg.Version).SingleOrDefaultAsync()
            ?? await db.Activities.IgnoreQueryFilters().Where(activity => activity.Id == id).Select(activity => (Guid?)activity.Version).SingleOrDefaultAsync()
            ?? await db.WeeklyGoals.IgnoreQueryFilters().Where(goal => goal.Id == id).Select(goal => (Guid?)goal.Version).SingleOrDefaultAsync()
            ?? await db.Routines.IgnoreQueryFilters().Where(routine => routine.Id == id).Select(routine => (Guid?)routine.Version).SingleOrDefaultAsync()
            ?? await db.RoutineDateRanges.IgnoreQueryFilters().Where(dates => dates.Id == id).Select(dates => (Guid?)dates.Version).SingleOrDefaultAsync());

    public static Task<List<LocationOpeningTime>> OpeningTimesOfAsync(TestAppFactory factory, Guid locationId) =>
        ReadAsync(factory, db => db.LocationOpeningTimes.IgnoreQueryFilters().AsNoTracking()
            .Where(time => time.LocationId == locationId).ToListAsync());

    /// <summary>The preferences saved by the user with this email address, or null if they haven't saved any.</summary>
    public static Task<UserPreferences?> PreferencesOfAsync(TestAppFactory factory, string email) =>
        ReadAsync(factory, db => PreferencesOf(db, email).SingleOrDefaultAsync());

    public static Task<int> PreferencesCountOfAsync(TestAppFactory factory, string email) =>
        ReadAsync(factory, db => PreferencesOf(db, email).CountAsync());

    /// <summary>The default buffers saved by the user with this email address, by category (ACC-6).</summary>
    public static Task<Dictionary<ActivityCategory, CategoryBuffers>> CategoryBuffersOfAsync(TestAppFactory factory, string email) =>
        ReadAsync(factory, db => db.CategoryBuffers.IgnoreQueryFilters().AsNoTracking()
            .Where(buffers => db.Users.Any(user => user.Id == buffers.OwnerId && user.Email == email))
            .ToDictionaryAsync(buffers => buffers.Category));

    private static IQueryable<UserPreferences> PreferencesOf(AppDbContext db, string email) =>
        db.UserPreferences.IgnoreQueryFilters().AsNoTracking()
            .Where(preferences => db.Users.Any(user => user.Id == preferences.OwnerId && user.Email == email));

    private static async Task<T> ReadAsync<T>(TestAppFactory factory, Func<AppDbContext, Task<T>> read)
    {
        using var scope = factory.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
