using FitnessTracker.Web.Data;
using FitnessTracker.Web.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FitnessTracker.Web.Tests.Data;

// Requirements §9 and CLAUDE.md rule 5: per-user isolation is enforced in the data layer itself, so a
// controller that forgets to filter, or a bug that builds the wrong entity, still can't reach another
// user's data. (The HTTP side of the same rule is T19, in Security/AccessControlTests.)
public sealed class OwnershipTests : IDisposable
{
    private const string Alex = "user-alex";
    private const string Sam = "user-sam";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public OwnershipTests()
    {
        _connection.Open();
        using var db = ContextFor(userId: null);
        db.Database.Migrate();
        db.Users.AddRange(
            new AppUser { Id = Alex, UserName = "alex@example.com", Email = "alex@example.com" },
            new AppUser { Id = Sam, UserName = "sam@example.com", Email = "sam@example.com" });
        db.SaveChanges();
    }

    [Fact]
    public void Queries_ReturnOnlyTheSignedInUsersRows()
    {
        var alexsGym = Add(Alex, "Alex's gym");
        var samsOffice = Add(Sam, "Sam's office");

        using var db = ContextFor(Alex);

        Assert.Equal(["Alex's gym"], db.Locations.Select(location => location.Name).ToList());
        Assert.NotNull(db.Locations.Find(alexsGym));
        Assert.Null(db.Locations.Find(samsOffice));
    }

    [Fact]
    public void Queries_ReturnNothing_WhenNobodyIsSignedIn()
    {
        Add(Alex, "Alex's gym");

        using var db = ContextFor(userId: null);

        Assert.Empty(db.Locations.ToList());
    }

    [Fact]
    public void SaveChanges_MakesTheSignedInUserTheOwnerOfNewRows_WhateverOwnerWasSet()
    {
        using (var db = ContextFor(Alex))
        {
            db.Locations.Add(new Location { Name = "Alex's gym", OwnerId = Sam });
            db.SaveChanges();
        }

        Assert.Equal(Alex, StoredOwnerOf("Alex's gym"));
    }

    [Fact]
    public void SaveChanges_RefusesNewRows_WhenNobodyIsSignedIn()
    {
        using var db = ContextFor(userId: null);
        db.Locations.Add(new Location { Name = "Nobody's gym" });

        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Fact]
    public void SaveChanges_RefusesToChangeAnotherUsersRow()
    {
        var alexsGym = Add(Alex, "Alex's gym");
        using var db = ContextFor(Sam);
        db.Locations.Attach(new Location { Id = alexsGym, OwnerId = Alex, Name = "Alex's gym" }).Entity.Name = "Changed by Sam";

        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Equal("Alex's gym", StoredNameOf(alexsGym));
    }

    // A bug that builds the entity with the wrong owner can fool a check made in memory, so the
    // database must refuse too: the update only applies where the stored owner matches.
    [Fact]
    public void SaveChanges_RefusesToChangeAnotherUsersRow_EvenWhenTheEntityClaimsToBeTheirs()
    {
        var alexsGym = Add(Alex, "Alex's gym");
        using var db = ContextFor(Sam);
        db.Locations.Attach(new Location { Id = alexsGym, OwnerId = Sam, Name = "Alex's gym" }).Entity.Name = "Changed by Sam";

        Assert.Throws<DbUpdateConcurrencyException>(() => db.SaveChanges());
        Assert.Equal("Alex's gym", StoredNameOf(alexsGym));
        Assert.Equal(Alex, StoredOwnerOf("Alex's gym"));
    }

    [Fact]
    public void SaveChanges_RefusesToDeleteAnotherUsersRow_EvenWhenTheEntityClaimsToBeTheirs()
    {
        var alexsGym = Add(Alex, "Alex's gym");
        using var db = ContextFor(Sam);
        db.Locations.Remove(new Location { Id = alexsGym, OwnerId = Sam, Name = "Alex's gym" });

        Assert.Throws<DbUpdateConcurrencyException>(() => db.SaveChanges());
        Assert.Equal("Alex's gym", StoredNameOf(alexsGym));
    }

    [Fact]
    public void SaveChanges_RefusesToGiveARowToAnotherUser()
    {
        var alexsGym = Add(Alex, "Alex's gym");
        using var db = ContextFor(Alex);
        db.Locations.Find(alexsGym)!.OwnerId = Sam;

        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Equal(Alex, StoredOwnerOf("Alex's gym"));
    }

    // ACC-9: deleting an account deletes all of its data. The database does it (cascade), so nothing is left
    // behind even though the app never loads those rows.
    [Fact]
    public void DeletingAnAccount_DeletesItsLocations_AndNobodyElses()
    {
        Add(Alex, "Alex's gym");
        Add(Alex, "Alex's office");
        Add(Sam, "Sam's office");

        using (var db = ContextFor(Alex))
        {
            db.Users.Remove(db.Users.Single(user => user.Id == Alex));
            db.SaveChanges();
        }

        using var check = ContextFor(userId: null);
        Assert.Equal(["Sam's office"], check.Locations.IgnoreQueryFilters().Select(location => location.Name).ToList());
    }

    // ACC-9 with CAL-9: an activity points at its routine, and a routine with activities can't be deleted on its own.
    // Deleting the account must still delete both, and the dates chosen for the routine, by the database's cascade,
    // without loading them.
    [Fact]
    public void DeletingAnAccount_DeletesItsRoutines_TheirDates_AndTheActivitiesInThem()
    {
        using (var db = ContextFor(Alex))
        {
            var routine = new Routine { Name = "Usual week", CycleWeeks = 2, WeekAStarts = new(2026, 10, 19), IsUsual = true };
            db.Routines.Add(routine);
            db.RoutineDateRanges.Add(new RoutineDateRange { RoutineId = routine.Id, StartDate = new(2026, 11, 2), EndDate = new(2026, 11, 8) });
            db.Activities.Add(new Activity
            {
                Title = "Gym",
                StartDate = new(2026, 10, 19),
                StartTime = new(7, 0),
                TimeZoneId = "Europe/London",
                LengthMinutes = 60,
                RepeatRule = "FREQ=WEEKLY;BYDAY=MO",
                RoutineId = routine.Id,
                RoutineWeek = 'A',
            });
            db.SaveChanges();
        }

        using (var db = ContextFor(Alex))
        {
            db.Users.Remove(db.Users.Single(user => user.Id == Alex));
            db.SaveChanges();
        }

        using var check = ContextFor(userId: null);
        Assert.Empty(check.Routines.IgnoreQueryFilters().ToList());
        Assert.Empty(check.RoutineDateRanges.IgnoreQueryFilters().ToList());
        Assert.Empty(check.Activities.IgnoreQueryFilters().ToList());
    }

    // Phase 4 adds more owned records (activities, series, logs); each must get the same protection
    // just by implementing IOwnedByUser.
    [Fact]
    public void EveryOwnedEntityType_HasTheOwnerFilterAndOwnerCheckedOnSave()
    {
        using var db = ContextFor(userId: null);
        var ownedTypes = db.Model.GetEntityTypes()
            .Where(type => typeof(IOwnedByUser).IsAssignableFrom(type.ClrType))
            .ToList();

        Assert.NotEmpty(ownedTypes);
        Assert.All(ownedTypes, type =>
        {
            Assert.Contains(type.GetDeclaredQueryFilters(), filter => filter.Key == AppDbContext.OwnerFilter);
            Assert.True(type.FindProperty(nameof(IOwnedByUser.OwnerId))!.IsConcurrencyToken);
        });
    }

    // The protection above only covers types that implement IOwnedByUser. A table of personal data that doesn't
    // (say, opening hours stored under a Location without their own OwnerId) would have no filter, and anyone
    // could query it directly. So every table outside Identity's own must be owned, or be listed here on purpose
    // as shared, non-personal data (such as a starter list of exercises).
    private static readonly Type[] SharedNonPersonalTables = [];

    [Fact]
    public void EveryTable_BelongsToAUser_UnlessItIsIdentitysOwnOrListedAsShared()
    {
        using var db = ContextFor(userId: null);
        var unprotected = db.Model.GetEntityTypes()
            .Select(type => type.ClrType)
            .Where(type => !typeof(IOwnedByUser).IsAssignableFrom(type))
            .Where(type => type != typeof(AppUser) && type.Namespace != "Microsoft.AspNetCore.Identity")
            .Except(SharedNonPersonalTables)
            .Select(type => type.Name)
            .ToList();

        Assert.Empty(unprotected);
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext ContextFor(string? userId) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options, new FixedCurrentUser(userId));

    private Guid Add(string ownerId, string name)
    {
        using var db = ContextFor(ownerId);
        var location = new Location { Name = name };
        db.Locations.Add(location);
        db.SaveChanges();
        return location.Id;
    }

    // What is really stored, read past the filter.
    private string StoredNameOf(Guid id)
    {
        using var db = ContextFor(userId: null);
        return db.Locations.IgnoreQueryFilters().Single(location => location.Id == id).Name;
    }

    private string StoredOwnerOf(string name)
    {
        using var db = ContextFor(userId: null);
        return db.Locations.IgnoreQueryFilters().Single(location => location.Name == name).OwnerId;
    }
}
