using FitnessTracker.Web.Data;
using FitnessTracker.Web.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FitnessTracker.Web.Tests.Data;

// Requirements §9 (reliability): two tabs editing the same record can't silently overwrite each other. Every save
// gives the row a new version, and a save made from an out-of-date copy is refused by the database.
public sealed class VersionTests : IDisposable
{
    private const string Alex = "user-alex";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public VersionTests()
    {
        _connection.Open();
        using var db = ContextFor(userId: null);
        db.Database.Migrate();
        db.Users.Add(new AppUser { Id = Alex, UserName = "alex@example.com", Email = "alex@example.com" });
        db.SaveChanges();
    }

    // Every kind of personal record can be edited, so each one gets this protection, not just locations.
    [Fact]
    public void EveryOwnedEntityType_HasAVersionCheckedOnSave()
    {
        using var db = ContextFor(userId: null);
        var ownedTypes = db.Model.GetEntityTypes()
            .Where(type => typeof(IOwnedByUser).IsAssignableFrom(type.ClrType))
            .ToList();

        Assert.NotEmpty(ownedTypes);
        Assert.All(ownedTypes, type =>
        {
            Assert.True(typeof(IVersioned).IsAssignableFrom(type.ClrType), $"{type.ClrType.Name} has no version.");
            Assert.True(type.FindProperty(nameof(IVersioned.Version))!.IsConcurrencyToken);
        });
    }

    [Fact]
    public void EverySave_GivesTheRowANewVersion()
    {
        var id = Add("Home gym");
        var versionWhenAdded = StoredVersionOf(id);

        using (var db = ContextFor(Alex))
        {
            db.Locations.Find(id)!.Name = "Town gym";
            db.SaveChanges();
        }

        Assert.NotEqual(Guid.Empty, versionWhenAdded);
        Assert.NotEqual(versionWhenAdded, StoredVersionOf(id));
    }

    [Fact]
    public void SavingFromAnOutOfDateCopy_IsRefused()
    {
        var id = Add("Home gym");
        using var firstTab = ContextFor(Alex);
        using var secondTab = ContextFor(Alex);
        var first = firstTab.Locations.Find(id)!;
        var second = secondTab.Locations.Find(id)!;

        first.Name = "Town gym";
        firstTab.SaveChanges();
        second.Name = "Gym by the station";

        Assert.Throws<DbUpdateConcurrencyException>(() => secondTab.SaveChanges());
        using var check = ContextFor(userId: null);
        Assert.Equal("Town gym", check.Locations.IgnoreQueryFilters().Single(location => location.Id == id).Name);
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext ContextFor(string? userId) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options, new FixedCurrentUser(userId));

    private Guid Add(string name)
    {
        using var db = ContextFor(Alex);
        var location = new Location { Name = name };
        db.Locations.Add(location);
        db.SaveChanges();
        return location.Id;
    }

    private Guid StoredVersionOf(Guid id)
    {
        using var db = ContextFor(userId: null);
        return db.Locations.IgnoreQueryFilters().Single(location => location.Id == id).Version;
    }
}
