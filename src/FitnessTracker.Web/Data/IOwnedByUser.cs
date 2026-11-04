namespace FitnessTracker.Web.Data;

/// <summary>
/// A record that belongs to one user. AppDbContext only ever returns the signed-in user's rows of these,
/// and refuses to save changes to anyone else's (CLAUDE.md rule 5; requirements ACC-2 and §9).
/// </summary>
public interface IOwnedByUser
{
    /// <summary>The owner's user id. Set by AppDbContext when the row is first saved.</summary>
    string OwnerId { get; set; }
}
