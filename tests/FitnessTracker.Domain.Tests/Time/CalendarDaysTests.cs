using FitnessTracker.Domain.Time;
using NodaTime;

namespace FitnessTracker.Domain.Tests.Time;

// T3 and requirements §8 ("crossing midnight"): an activity belongs to the day it starts, for the calendar and
// targets, but its minutes count towards each day's load for the part that falls on that day.
public class CalendarDaysTests
{
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];

    [Fact]
    public void AnActivityCrossingMidnight_BelongsToTheDayItStarts() // T3
    {
        var activity = Activity(new(2026, 10, 13, 22, 30), new(2026, 10, 14, 0, 30));

        Assert.Equal(new LocalDate(2026, 10, 13), CalendarDays.DayOf(activity, London));
    }

    [Fact]
    public void AnActivityCrossingMidnight_CountsTowardsEachDay_ForItsPartOfThatDay() // T3
    {
        var activity = Activity(new(2026, 10, 13, 22, 30), new(2026, 10, 14, 0, 30));

        DayShare[] expected = [new(new(2026, 10, 13), Duration.FromMinutes(90)), new(new(2026, 10, 14), Duration.FromMinutes(30))];
        Assert.Equal(expected, CalendarDays.Split(activity, London));
    }

    [Fact]
    public void AnActivityWithinOneDay_CountsOnlyTowardsThatDay()
    {
        var activity = Activity(new(2026, 10, 13, 7, 0), new(2026, 10, 13, 8, 0));

        DayShare[] expected = [new(new(2026, 10, 13), Duration.FromHours(1))];
        Assert.Equal(expected, CalendarDays.Split(activity, London));
    }

    // Intervals are half-open: ending at midnight puts nothing, not even an empty share, on the next day.
    [Fact]
    public void AnActivityEndingAtMidnight_CountsOnlyTowardsTheDayItStarts()
    {
        var activity = Activity(new(2026, 10, 13, 22, 0), new(2026, 10, 14, 0, 0));

        DayShare[] expected = [new(new(2026, 10, 13), Duration.FromHours(2))];
        Assert.Equal(expected, CalendarDays.Split(activity, London));
    }

    // Sunday 25 October 2026 lasts 25 hours: 23:00 Saturday to 03:00 Sunday is 5 hours, 4 of them on Sunday.
    [Fact]
    public void OnTheNightTheClocksGoBack_TheRepeatedHourCountsTowardsSunday()
    {
        var activity = Activity(new(2026, 10, 24, 23, 0), new(2026, 10, 25, 3, 0));

        DayShare[] expected = [new(new(2026, 10, 24), Duration.FromHours(1)), new(new(2026, 10, 25), Duration.FromHours(4))];
        Assert.Equal(expected, CalendarDays.Split(activity, London));
    }

    [Fact]
    public void AnActivitySpanningTwoMidnights_CountsAFullDayInTheMiddle()
    {
        var activity = Activity(new(2026, 10, 12, 22, 0), new(2026, 10, 14, 2, 0));

        DayShare[] expected =
        [
            new(new(2026, 10, 12), Duration.FromHours(2)),
            new(new(2026, 10, 13), Duration.FromHours(24)),
            new(new(2026, 10, 14), Duration.FromHours(2)),
        ];
        Assert.Equal(expected, CalendarDays.Split(activity, London));
    }

    // Sunday 28 March 2027 lasts 23 hours, so a span covering all of it counts 23 hours, not 24.
    [Fact]
    public void AnActivitySpanningTheWholeDayTheClocksGoForward_Counts23HoursForThatDay()
    {
        var activity = Activity(new(2027, 3, 27, 22, 0), new(2027, 3, 29, 2, 0));

        DayShare[] expected =
        [
            new(new(2027, 3, 27), Duration.FromHours(2)),
            new(new(2027, 3, 28), Duration.FromHours(23)),
            new(new(2027, 3, 29), Duration.FromHours(2)),
        ];
        Assert.Equal(expected, CalendarDays.Split(activity, London));
    }

    private static Interval Activity(LocalDateTime start, LocalDateTime end) => ClockTimes.ToInterval(start, end, London);
}
