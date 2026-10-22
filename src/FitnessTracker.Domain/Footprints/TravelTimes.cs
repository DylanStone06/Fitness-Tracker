using System.Collections.Frozen;
using NodaTime;

namespace FitnessTracker.Domain.Footprints;

/// <summary>A journey in one direction between two locations (TRV-2: home to gym may differ from gym to home).</summary>
public readonly record struct Leg(Guid From, Guid To);

/// <summary>
/// The journeys an activity needs, or null with the legs that are missing. A missing leg is never treated as zero
/// minutes, which would hide conflicts with the time really needed (P1).
/// </summary>
public sealed record JourneyLookup(Journeys? Journeys, IReadOnlyList<Leg> MissingLegs);

/// <summary>
/// A person's travel legs: how long each one-way journey between two of their locations takes. Decides the journeys
/// around an activity (requirements §7.1): none when it is at base or has no location; otherwise the leg from base to
/// it and the leg from it back to base.
/// </summary>
public sealed class TravelTimes(IReadOnlyDictionary<Leg, Duration> legs)
{
    public static TravelTimes None { get; } = new(new Dictionary<Leg, Duration>());

    public IReadOnlyDictionary<Leg, Duration> Legs { get; } = legs.ToFrozenDictionary();

    public JourneyLookup JourneysFor(Guid? activityAt, Guid baseLocation)
    {
        if (activityAt is not { } location || location == baseLocation)
        {
            return new JourneyLookup(Journeys.None, []);
        }

        var there = new Leg(baseLocation, location);
        var back = new Leg(location, baseLocation);
        var missing = new[] { there, back }.Where(leg => !Legs.ContainsKey(leg)).ToList();
        return missing.Count > 0
            ? new JourneyLookup(null, missing)
            : new JourneyLookup(new Journeys(Out: Legs[there], Back: Legs[back]), []);
    }
}
