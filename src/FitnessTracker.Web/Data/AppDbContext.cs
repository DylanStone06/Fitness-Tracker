using System.Linq.Expressions;
using FitnessTracker.Web.Services;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FitnessTracker.Web.Data;

/// <summary>
/// The app's database. Per-user isolation (CLAUDE.md rule 5) is enforced here rather than in each
/// controller, in three layers:
/// 1. reads: a query filter returns only the signed-in user's rows of every <see cref="IOwnedByUser"/>;
/// 2. saves: new rows are stamped with the signed-in user, and changing or deleting anyone else's is refused;
/// 3. the database: OwnerId is a concurrency token, so an UPDATE or DELETE only applies where the stored
///    owner matches, even if a bug built the entity with the wrong owner.
/// It also gives every <see cref="IVersioned"/> record a new Version on each save, checked the same way, so two
/// tabs can't silently overwrite each other (requirements §9). Both checks raise DbUpdateConcurrencyException: read
/// the row again through the filter to tell "gone or not yours" from "changed elsewhere".
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
    : IdentityDbContext<AppUser>(options)
{
    /// <summary>The name of the per-user query filter (for the rare, deliberate IgnoreQueryFilters).</summary>
    public const string OwnerFilter = "Owner";

    public DbSet<Location> Locations => Set<Location>();

    public DbSet<LocationOpeningTime> LocationOpeningTimes => Set<LocationOpeningTime>();

    public DbSet<TravelLeg> TravelLegs => Set<TravelLeg>();

    public DbSet<UserPreferences> UserPreferences => Set<UserPreferences>();

    public DbSet<Activity> Activities => Set<Activity>();

    // Read by the query filter each time a query runs, so every context filters by its own signed-in user.
    // Null when nobody is signed in: the filter then matches no rows.
    private string? CurrentUserId => currentUser.UserId;

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        CheckOwnership();
        GiveNewVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        CheckOwnership();
        GiveNewVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Location>(location =>
        {
            location.Property(l => l.Name).HasMaxLength(Location.NameMaxLength);
            // Deleting an account deletes its locations (ACC-9).
            location.HasOne<AppUser>().WithMany().HasForeignKey(l => l.OwnerId).OnDelete(DeleteBehavior.Cascade);
            // Deleting a location deletes its opening times.
            location.HasMany(l => l.OpeningTimes).WithOne().HasForeignKey(time => time.LocationId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<LocationOpeningTime>(time =>
        {
            // One opening per weekday.
            time.HasIndex(t => new { t.LocationId, t.Day }).IsUnique();
            time.HasOne<AppUser>().WithMany().HasForeignKey(t => t.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TravelLeg>(leg =>
        {
            // One time per direction between two locations.
            leg.HasIndex(l => new { l.FromLocationId, l.ToLocationId }).IsUnique();
            // Deleting either location deletes the legs to and from it.
            leg.HasOne<Location>().WithMany().HasForeignKey(l => l.FromLocationId).OnDelete(DeleteBehavior.Cascade);
            leg.HasOne<Location>().WithMany().HasForeignKey(l => l.ToLocationId).OnDelete(DeleteBehavior.Cascade);
            leg.HasOne<AppUser>().WithMany().HasForeignKey(l => l.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<UserPreferences>(preferences =>
        {
            // One set per person, even if two tabs save for the first time at once.
            preferences.HasIndex(p => p.OwnerId).IsUnique();
            preferences.Property(p => p.TimeZoneId).HasMaxLength(64);
            preferences.HasOne<AppUser>().WithMany().HasForeignKey(p => p.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Activity>(activity =>
        {
            activity.Property(a => a.Title).HasMaxLength(Activity.TitleMaxLength);
            activity.Property(a => a.Notes).HasMaxLength(Activity.NotesMaxLength);
            activity.Property(a => a.RepeatRule).HasMaxLength(Activity.RepeatRuleMaxLength);
            activity.Property(a => a.TimeZoneId).HasMaxLength(64);
            // Deleting a location keeps its activities, with no location.
            activity.HasOne<Location>().WithMany().HasForeignKey(a => a.LocationId).OnDelete(DeleteBehavior.SetNull);
            activity.HasOne<AppUser>().WithMany().HasForeignKey(a => a.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });

        // Every owned entity type gets the same protection, so a new one can't be forgotten.
        var ownedTypes = builder.Model.GetEntityTypes()
            .Where(type => typeof(IOwnedByUser).IsAssignableFrom(type.ClrType))
            .ToList();
        foreach (var type in ownedTypes)
        {
            builder.Entity(type.ClrType).HasQueryFilter(OwnerFilter, BelongsToCurrentUser(type.ClrType));
            builder.Entity(type.ClrType).Property(nameof(IOwnedByUser.OwnerId)).IsConcurrencyToken();
        }

        // Requirements §9: an UPDATE or DELETE only applies where the stored Version is the one the change was
        // made from, so an out-of-date tab can't silently overwrite a newer save.
        var versionedTypes = builder.Model.GetEntityTypes()
            .Where(type => typeof(IVersioned).IsAssignableFrom(type.ClrType))
            .ToList();
        foreach (var type in versionedTypes)
        {
            builder.Entity(type.ClrType).Property(nameof(IVersioned.Version)).IsConcurrencyToken();
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<NodaTime.LocalTime>().HaveConversion<LocalTimeConverter>();
        configurationBuilder.Properties<NodaTime.LocalDate>().HaveConversion<LocalDateConverter>();
    }

    /// <summary>Builds <c>entity => entity.OwnerId == this.CurrentUserId</c> for the given entity type.</summary>
    private LambdaExpression BelongsToCurrentUser(Type entityType)
    {
        var entity = Expression.Parameter(entityType, "entity");
        return Expression.Lambda(
            Expression.Equal(
                Expression.Property(entity, nameof(IOwnedByUser.OwnerId)),
                Expression.Property(Expression.Constant(this), nameof(CurrentUserId))),
            entity);
    }

    // The version the change was made from stays as the original value, which the database checks; the row is
    // saved with a new one.
    private void GiveNewVersions()
    {
        foreach (var entry in ChangeTracker.Entries<IVersioned>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.Version = Guid.NewGuid();
            }
        }
    }

    private void CheckOwnership()
    {
        foreach (var entry in ChangeTracker.Entries<IOwnedByUser>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.OwnerId = CurrentUserId
                        ?? throw new InvalidOperationException("Can't save personal data when nobody is signed in.");
                    break;

                case EntityState.Modified or EntityState.Deleted:
                    var originalOwner = entry.Property(e => e.OwnerId).OriginalValue;
                    if (CurrentUserId is null || originalOwner != CurrentUserId || entry.Entity.OwnerId != originalOwner)
                    {
                        throw new InvalidOperationException("Can't change or delete another user's data, or give data to another user.");
                    }

                    break;
            }
        }
    }
}
