using FitnessTracker.Web.Data;
using FitnessTracker.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FitnessTracker.Web.Tests.Data;

public sealed class MigrationTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    // Catches a change to the data model (an entity, a property, Identity's options) committed
    // without the migration that brings real databases up to date. The context comes from the app's
    // own services, so the model is exactly the one the running app uses.
    [Fact]
    public void Migrations_MatchTheModel()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(db.Database.HasPendingModelChanges(),
            "The model has changed since the last migration. Run: dev\\ft-dev.ps1 migration -Name <WhatChanged>");
    }

    // Catches the test database being built some other way (e.g. EnsureCreated), which would let
    // tests pass against a schema that real databases never get.
    [Fact]
    public void TestDatabase_IsBuiltFromTheMigrations()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.NotEmpty(db.Database.GetAppliedMigrations());
        Assert.Empty(db.Database.GetPendingMigrations());
    }
}
