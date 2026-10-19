using FitnessTracker.Domain.Locations;
using NodaTime;

namespace FitnessTracker.Web.Data;

/// <summary>
/// A place where activities happen: home, office, gym (TRV-1). One location can be home: the base that travel is
/// measured from (§7.1). Opening hours are optional: without them the location is always open.
/// </summary>
public class Location : IOwnedByUser, IVersioned
{
    public const int NameMaxLength = 100;

    // Version 7 GUIDs sort by creation time (ADR 0001).
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string OwnerId { get; set; } = "";

    public string Name { get; set; } = "";

    public Guid Version { get; set; }

    /// <summary>Home: where the day starts and ends, and travel is measured from. At most one per person.</summary>
    public bool IsHome { get; set; }

    /// <summary>False: no set opening hours, always open. True: open only as <see cref="OpeningTimes"/> say.</summary>
    public bool HasOpeningHours { get; set; }

    /// <summary>One row per open weekday; a weekday without a row is closed. Ignored when there are no set hours.</summary>
    public List<LocationOpeningTime> OpeningTimes { get; set; } = [];

    /// <summary>The domain's view of these opening hours, for the H4 check.</summary>
    public OpeningHours ToOpeningHours() =>
        HasOpeningHours
            ? new OpeningHours(OpeningTimes.ToDictionary(time => time.Day, time => new OpenTimes(time.Opens, time.Closes)))
            : OpeningHours.Always;
}

/// <summary>
/// When a location is open on one weekday. Closing at or before opening means after midnight. Its own OwnerId, so the
/// per-user filter protects it even when queried on its own (docs/architecture.md, rule 1).
/// </summary>
public class LocationOpeningTime : IOwnedByUser, IVersioned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string OwnerId { get; set; } = "";

    public Guid Version { get; set; }

    public Guid LocationId { get; set; }

    public IsoDayOfWeek Day { get; set; }

    public LocalTime Opens { get; set; }

    public LocalTime Closes { get; set; }
}
