namespace FitnessTracker.Web.Data;

/// <summary>
/// A place where activities happen: home, office, gym (TRV-1). Phase 2 stores only the name, so that
/// per-user isolation (T19) has a real record to protect; opening hours and travel legs arrive in Phase 4.
/// </summary>
public class Location : IOwnedByUser
{
    public const int NameMaxLength = 100;

    // Version 7 GUIDs sort by creation time (ADR 0001).
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string OwnerId { get; set; } = "";

    public string Name { get; set; } = "";
}
