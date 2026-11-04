using FitnessTracker.Domain.Footprints;
using NodaTime;

namespace FitnessTracker.Web.Data;

/// <summary>How a journey is made (TRV-2: a mode label). Only shown; it doesn't change the maths.</summary>
public enum TravelMode
{
    Walk,
    Cycle,
    Drive,
    Bus,
    Train,
    Other,
}

/// <summary>
/// TRV-2: how long the journey takes from one of a person's locations to another, in that direction only: home to
/// gym may differ from gym to home. Used for the travel parts of footprints (§7.1).
/// </summary>
public class TravelLeg : IOwnedByUser, IVersioned
{
    public const int MaxMinutes = 600;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string OwnerId { get; set; } = "";

    public Guid Version { get; set; }

    public Guid FromLocationId { get; set; }

    public Guid ToLocationId { get; set; }

    public int Minutes { get; set; }

    public TravelMode Mode { get; set; }

    /// <summary>The domain's view of a person's travel legs, for working out the journeys around an activity.</summary>
    public static TravelTimes ToTravelTimes(IEnumerable<TravelLeg> legs) =>
        new(legs.ToDictionary(leg => new Leg(leg.FromLocationId, leg.ToLocationId), leg => Duration.FromMinutes(leg.Minutes)));
}
