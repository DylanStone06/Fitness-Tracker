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
