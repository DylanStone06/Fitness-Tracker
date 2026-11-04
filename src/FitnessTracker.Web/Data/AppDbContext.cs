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
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
    : IdentityDbContext<AppUser>(options)
{
    /// <summary>The name of the per-user query filter (for the rare, deliberate IgnoreQueryFilters).</summary>
    public const string OwnerFilter = "Owner";

    public DbSet<Location> Locations => Set<Location>();

    // Read by the query filter each time a query runs, so every context filters by its own signed-in user.
    // Null when nobody is signed in: the filter then matches no rows.
    private string? CurrentUserId => currentUser.UserId;

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        CheckOwnership();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        CheckOwnership();
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
