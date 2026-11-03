using FitnessTracker.Domain.Workouts;
using NodaTime;

namespace FitnessTracker.Domain.Tests.Workouts;

// FIT-3, Q25: pace is worked out from a run's time and distance each time it is shown, never stored, per km or per mile
// as chosen in Preferences ("5:30 per km").
public class PaceTests
{
    [Fact]
    public void Pace_IsTheTimeForEachKm()
    {
        Assert.Equal(Duration.FromMinutes(5), Pace.Per(Duration.FromMinutes(50), 10_000, DistanceUnit.Kilometres));
    }

    [Theory]
    [InlineData(28 * 60 + 36, 5200, DistanceUnit.Kilometres, "5:30 per km")]
    [InlineData(40 * 60, 8047, DistanceUnit.Miles, "8:00 per mile")] // 5 miles, stored as 8047 m: 7:59.98 a mile
    [InlineData(17 * 60 + 59, 3000, DistanceUnit.Kilometres, "6:00 per km")] // 5:59.67: never "5:60"
    [InlineData(65 * 60, 1000, DistanceUnit.Kilometres, "1:05:00 per km")]
    public void Pace_IsRoundedToTheSecond_ThenWritten(int seconds, int metres, DistanceUnit unit, string text)
    {
        Assert.Equal(text, Pace.Text(Duration.FromSeconds(seconds), metres, unit));
    }

    // Without both a time and a distance there is no pace.
    [Theory]
    [InlineData(0, 5000)]
    [InlineData(1800, 0)]
    public void WithoutATimeOrADistance_ThereIsNoPace(int seconds, int metres)
    {
        Assert.Null(Pace.Per(Duration.FromSeconds(seconds), metres, DistanceUnit.Kilometres));
        Assert.Null(Pace.Text(Duration.FromSeconds(seconds), metres, DistanceUnit.Kilometres));
    }

    // A time as a stopwatch shows it: minutes and seconds, with hours once there is one.
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(45, "0:45")]
    [InlineData(28 * 60 + 36, "28:36")]
    [InlineData(65 * 60 + 20, "1:05:20")]
    [InlineData(24 * 3600, "24:00:00")]
    public void StopwatchText_IsMinutesAndSeconds_WithHoursOnceThereIsOne(int seconds, string text)
    {
        Assert.Equal(text, StopwatchText.Format(Duration.FromSeconds(seconds)));
    }

    // ... to the nearest second, so 59:59.6 is a whole hour, not "59:60".
    [Fact]
    public void StopwatchText_RoundsToTheNearestSecond()
    {
        Assert.Equal("1:00:00", StopwatchText.Format(Duration.FromMilliseconds(3_599_600)));
    }
}
