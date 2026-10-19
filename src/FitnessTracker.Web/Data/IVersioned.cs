namespace FitnessTracker.Web.Data;

/// <summary>
/// A record that two tabs could edit at once (requirements §9). AppDbContext gives it a new Version on every save,
/// and the database only applies a change made from the version it was read at, so an out-of-date tab can't
/// silently overwrite a newer save.
/// </summary>
public interface IVersioned
{
    /// <summary>Changes on every save. An edit form carries the version it was opened with.</summary>
    Guid Version { get; set; }
}
