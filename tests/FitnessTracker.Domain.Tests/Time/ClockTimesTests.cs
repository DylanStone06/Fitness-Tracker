using FitnessTracker.Domain.Time;
using NodaTime;

namespace FitnessTracker.Domain.Tests.Time;

// Requirements §8: what a clock time means on the nights the clocks change. Europe/London's clocks go back
// at 02:00 BST on 25 October 2026 (01:00-01:59 happens twice) and forward at 01:00 GMT on 28 March 2027
// (01:00-01:59 never happens). Expected moments are written out in UTC by hand: BST is UTC+1, GMT is UTC+0.
public class ClockTimesTests
{
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];

    [Fact]
    public void AnOrdinaryClockTime_IsThatTimeInTheZone()
    {
        var moment = ClockTimes.ToInstant(new LocalDateTime(2026, 10, 13, 7, 0), London);

        Assert.Equal(Instant.FromUtc(2026, 10, 13, 6, 0), moment);
    }

    // T5: a time the clocks skip moves forward by the gap.
    [Fact]
    public void ATimeTheClocksSkip_MovesForwardByTheGap()
    {
        var moment = ClockTimes.ToInstant(new LocalDateTime(2027, 3, 28, 1, 30), London);

        Assert.Equal(Instant.FromUtc(2027, 3, 28, 1, 30), moment); // 02:30 BST
    }

    // T4: a time that happens twice means the first, earlier one.
    [Fact]
    public void ATimeThatHappensTwice_IsTheFirstOne()
    {
        var moment = ClockTimes.ToInstant(new LocalDateTime(2026, 10, 25, 1, 30), London);

        Assert.Equal(Instant.FromUtc(2026, 10, 25, 0, 30), moment); // 01:30 BST, not 01:30 GMT
    }

    // T4 and T5: durations are elapsed time, so the same clock times can mean a longer or shorter night.
    [Theory]
    [InlineData(2026, 10, 24, 9)] // T4: the night the clocks go back
    [InlineData(2027, 3, 27, 7)] // T5: the night the clocks go forward
    [InlineData(2026, 10, 17, 8)] // an ordinary night
    public void SleepFrom2300To0700_LastsTheTimeThatReallyPassed(int year, int month, int day, int expectedHours)
    {
        var bedtime = new LocalDateTime(year, month, day, 23, 0);
        var wakeTime = bedtime.Date.PlusDays(1).At(new LocalTime(7, 0));

        var night = ClockTimes.ToInterval(bedtime, wakeTime, London);

        Assert.Equal(Duration.FromHours(expectedHours), night.Duration);
    }
}
