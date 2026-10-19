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

    /// <summary>The preferences saved by the user with this email address, or null if they haven't saved any.</summary>
    public static Task<UserPreferences?> PreferencesOfAsync(TestAppFactory factory, string email) =>
        ReadAsync(factory, db => PreferencesOf(db, email).SingleOrDefaultAsync());

    public static Task<int> PreferencesCountOfAsync(TestAppFactory factory, string email) =>
        ReadAsync(factory, db => PreferencesOf(db, email).CountAsync());

    private static IQueryable<UserPreferences> PreferencesOf(AppDbContext db, string email) =>
        db.UserPreferences.IgnoreQueryFilters().AsNoTracking()
            .Where(preferences => db.Users.Any(user => user.Id == preferences.OwnerId && user.Email == email));

    private static async Task<T> ReadAsync<T>(TestAppFactory factory, Func<AppDbContext, Task<T>> read)
    {
        using var scope = factory.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
