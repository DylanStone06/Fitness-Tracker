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
        ReadLocationsAsync(factory, locations => locations.SingleOrDefaultAsync(location => location.Name == name));

    public static Task<int> LocationCountAsync(TestAppFactory factory) =>
        ReadLocationsAsync(factory, locations => locations.CountAsync());

    private static async Task<T> ReadLocationsAsync<T>(TestAppFactory factory, Func<IQueryable<Location>, Task<T>> read)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await read(db.Locations.IgnoreQueryFilters().AsNoTracking());
    }
}
