using FitnessTracker.Domain.Footprints;
using NodaTime;

namespace FitnessTracker.Domain.Tests.Footprints;

// Requirements §7.1 and TRV-2: travel applies when the activity's location differs from the base location, using
// the legs in each direction (home to gym may differ from gym to home). A leg that hasn't been entered is never
// treated as zero minutes, which would hide conflicts (P1).
public class TravelTimesTests
{
    private static readonly Guid Home = Guid.Parse("0199e3a0-0000-7000-8000-000000000001");
    private static readonly Guid Gym = Guid.Parse("0199e3a0-0000-7000-8000-000000000002");
    private static readonly Guid Office = Guid.Parse("0199e3a0-0000-7000-8000-000000000003");

    private static readonly TravelTimes Legs = new(new Dictionary<Leg, Duration>
    {
        [new Leg(Home, Gym)] = Duration.FromMinutes(25),
        [new Leg(Gym, Home)] = Duration.FromMinutes(45),
        [new Leg(Home, Office)] = Duration.FromMinutes(30),
    });

    [Fact]
    public void AnActivityAtBase_HasNoTravel()
    {
        Assert.Equal(new JourneyLookup(Journeys.None, []), Legs.JourneysFor(activityAt: Home, baseLocation: Home));
    }

    // An activity with no location (a phone call, say) involves no journey.
    [Fact]
    public void AnActivityWithNoLocation_HasNoTravel()
    {
        Assert.Equal(new JourneyLookup(Journeys.None, []), Legs.JourneysFor(activityAt: null, baseLocation: Home));
    }

    [Fact]
    public void AnActivityAwayFromBase_UsesTheLegInEachDirection()
    {
        var lookup = Legs.JourneysFor(activityAt: Gym, baseLocation: Home);

        Assert.Equal(new Journeys(Out: Duration.FromMinutes(25), Back: Duration.FromMinutes(45)), lookup.Journeys);
        Assert.Empty(lookup.MissingLegs);
    }

    [Fact]
    public void AMissingLeg_GivesNoJourneys_AndSaysWhichLegIsMissing()
    {
        var lookup = Legs.JourneysFor(activityAt: Office, baseLocation: Home);

        Assert.Null(lookup.Journeys);
        Assert.Equal([new Leg(Office, Home)], lookup.MissingLegs);
    }

    [Fact]
    public void WithNoLegsAtAll_BothDirectionsAreMissing_ThereFirst()
    {
        var lookup = TravelTimes.None.JourneysFor(activityAt: Gym, baseLocation: Home);

        Assert.Null(lookup.Journeys);
        Assert.Equal([new Leg(Home, Gym), new Leg(Gym, Home)], lookup.MissingLegs);
    }
}
