using FitnessTracker.Web.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FitnessTracker.Web.Tests.Data;

public sealed class ConnectionStringTests
{
    // Without a configured database the app must stop with a message naming the missing setting,
    // never quietly create a database file somewhere (such as inside the repository).
    [Fact]
    public void Database_RefusesToOpen_WhenNoConnectionStringIsConfigured()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        using var scope = factory.Services.CreateScope();

        var error = Assert.Throws<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<AppDbContext>());

        Assert.Contains("ConnectionStrings:Default", error.Message);
    }
}
