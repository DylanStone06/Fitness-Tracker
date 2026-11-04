using FitnessTracker.Domain.Locations;
using FitnessTracker.Web.Data;
using NodaTime;

namespace FitnessTracker.Web.Tests.Data;

// The stored form of a location's opening hours turned into the domain's OpeningHours (TRV-1, H4). "No set hours"
// must become always open, never "closed every day" (an empty list of days would mean that).
public sealed class LocationTests
{
    [Fact]
    public void ALocationWithoutOpeningHours_IsAlwaysOpen()
    {
        var location = new Location { HasOpeningHours = false };

        Assert.True(location.ToOpeningHours().IsAlwaysOpen);
    }

    // TRV-2: stored legs become the domain's travel times, each in its own direction.
    [Fact]
    public void TravelLegs_BecomeTheDomainsTravelTimes_InTheirOwnDirection()
    {
        var home = Guid.Parse("0199e3a0-0000-7000-8000-000000000001");
        var gym = Guid.Parse("0199e3a0-0000-7000-8000-000000000002");
        TravelLeg[] legs =
        [
            new() { FromLocationId = home, ToLocationId = gym, Minutes = 25 },
            new() { FromLocationId = gym, ToLocationId = home, Minutes = 45 },
        ];

        var lookup = TravelLeg.ToTravelTimes(legs).JourneysFor(activityAt: gym, baseLocation: home);

        Assert.Equal(new FitnessTracker.Domain.Footprints.Journeys(Duration.FromMinutes(25), Duration.FromMinutes(45)), lookup.Journeys);
    }

    [Fact]
    public void ALocationWithOpeningHours_IsOpenOnItsListedDaysOnly()
    {
        var location = new Location
        {
            HasOpeningHours = true,
            OpeningTimes = [new LocationOpeningTime { Day = IsoDayOfWeek.Friday, Opens = new LocalTime(18, 0), Closes = new LocalTime(2, 0) }],
        };

        var hours = location.ToOpeningHours();

        Assert.False(hours.IsAlwaysOpen);
        Assert.Equal(new Dictionary<IsoDayOfWeek, OpenTimes> { [IsoDayOfWeek.Friday] = new(new LocalTime(18, 0), new LocalTime(2, 0)) }, hours.Days);
    }
}
