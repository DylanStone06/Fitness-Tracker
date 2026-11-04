using FitnessTracker.Web.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodaTime;

namespace FitnessTracker.Web.Tests.Infrastructure;

/// <summary>
/// Runs the real app in memory against its own private, in-memory SQLite database, built from the
/// migrations. Each factory instance (one per test class) gets a fresh database.
/// </summary>
public sealed class TestAppFactory : WebApplicationFactory<Program>
{
    // A named in-memory database lives as long as at least one connection to it is open; this one
    // stays open for the factory's lifetime so the app's own connections all see the same data.
    private readonly string _connectionString = $"Data Source=ft-tests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;

    public TestAppFactory()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Never "Development": that would load the developer's user-secrets, which point at their real database.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        // "Today" is fixed, so checks that look ahead from today (the activity check's 8 weeks) give the same result
        // whenever the tests run.
        builder.ConfigureTestServices(services => services.AddSingleton<IClock>(FixedClock.TestsToday));
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            // Pooled connections the app opened would otherwise keep this in-memory database alive
            // until the whole test run ends.
            SqliteConnection.ClearPool(_keepAlive);
            _keepAlive.Dispose();
        }
    }
}
