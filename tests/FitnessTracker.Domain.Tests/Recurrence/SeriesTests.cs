using FitnessTracker.Domain.Recurrence;
using NodaTime;

namespace FitnessTracker.Domain.Tests.Recurrence;

// CAL-3 and T7: which dates a series falls on, worked out across several months. October 2026 starts on a
// Thursday; Europe/London's clocks go back on 25 October 2026 and forward on 28 March 2027. Expected moments
// are written in UTC by hand (BST is UTC+1, GMT is UTC+0, New York in October is UTC-4).
public class SeriesTests
{
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly DateTimeZone NewYork = DateTimeZoneProviders.Tzdb["America/New_York"];
    private static readonly LocalTime SevenAm = new(7, 0);
    private static readonly Duration FiftyMinutes = Duration.FromMinutes(50);

    [Fact]
    public void Weekly_OnChosenDays() // T7
    {
        var gym = new Series(new(2026, 10, 5), SevenAm, FiftyMinutes, London,
            RecurrenceRule.Weekly([IsoDayOfWeek.Monday, IsoDayOfWeek.Wednesday, IsoDayOfWeek.Friday]));

        LocalDate[] expected =
        [
            new(2026, 10, 5), new(2026, 10, 7), new(2026, 10, 9),
            new(2026, 10, 12), new(2026, 10, 14), new(2026, 10, 16),
            new(2026, 10, 19), new(2026, 10, 21), new(2026, 10, 23),
            new(2026, 10, 26), new(2026, 10, 28), new(2026, 10, 30),
        ];
        Assert.Equal(expected, DatesOf(gym, new(2026, 10, 1), new(2026, 10, 31)));
    }

    [Fact]
    public void Fortnightly_AcrossSeveralMonths() // T7
    {
        var run = new Series(new(2026, 10, 6), SevenAm, FiftyMinutes, London,
            RecurrenceRule.Weekly([IsoDayOfWeek.Tuesday], everyWeeks: 2));

        LocalDate[] expected =
        [
            new(2026, 10, 6), new(2026, 10, 20), new(2026, 11, 3), new(2026, 11, 17),
            new(2026, 12, 1), new(2026, 12, 15), new(2026, 12, 29),
        ];
        Assert.Equal(expected, DatesOf(run, new(2026, 10, 1), new(2026, 12, 31)));
    }

    [Fact]
    public void EveryFewDays()
    {
        var stretch = new Series(new(2026, 10, 1), SevenAm, FiftyMinutes, London, RecurrenceRule.Daily(everyDays: 3));

        LocalDate[] expected = [new(2026, 10, 1), new(2026, 10, 4), new(2026, 10, 7), new(2026, 10, 10)];
        Assert.Equal(expected, DatesOf(stretch, new(2026, 10, 1), new(2026, 10, 10)));
    }

    [Fact]
    public void EndingOnADate_IncludesThatDate() // T7
    {
        var course = new Series(new(2026, 10, 5), SevenAm, FiftyMinutes, London,
            RecurrenceRule.Daily().EndingOn(new(2026, 10, 9)));

        LocalDate[] expected = [new(2026, 10, 5), new(2026, 10, 6), new(2026, 10, 7), new(2026, 10, 8), new(2026, 10, 9)];
        Assert.Equal(expected, DatesOf(course, new(2026, 10, 1), new(2026, 10, 31)));
    }

    // The count runs from the series' first date, not from the start of the dates asked for: the five sessions
    // are Thu 1, Mon 5, Thu 8, Mon 12 and Thu 15 October.
    [Fact]
    public void EndingAfterACount_CountsFromTheFirstDate() // T7
    {
        var physio = new Series(new(2026, 10, 1), SevenAm, FiftyMinutes, London,
            RecurrenceRule.Weekly([IsoDayOfWeek.Monday, IsoDayOfWeek.Thursday]).EndingAfter(5));

        LocalDate[] expected = [new(2026, 10, 12), new(2026, 10, 15)];
        Assert.Equal(expected, DatesOf(physio, new(2026, 10, 10), new(2026, 12, 31)));
    }

    // Only the days the rule falls on are counted, not the days in between.
    [Fact]
    public void EveryFewDays_EndingAfterACount()
    {
        var stretch = new Series(new(2026, 10, 1), SevenAm, FiftyMinutes, London, RecurrenceRule.Daily(everyDays: 3).EndingAfter(3));

        LocalDate[] expected = [new(2026, 10, 1), new(2026, 10, 4), new(2026, 10, 7)];
        Assert.Equal(expected, DatesOf(stretch, new(2026, 10, 1), new(2026, 10, 31)));
    }

    // Asking for dates long after a series began gives the same answer as walking from its first date. A
    // fortnightly series from Tuesday 7 January 2025: Monday 6 January 2025 to Monday 5 October 2026 is
    // 365 + 272 = 637 days = 91 weeks (odd), so in October 2026 it falls on the 13th and 27th (checked by hand and
    // with .NET's own date maths). Asked from the 5th, an odd week, so counting weeks from the dates asked for,
    // instead of from the first date, would wrongly give the 6th and 20th.
    [Fact]
    public void Fortnightly_AskedForLongAfterItBegan_KeepsItsWeeks()
    {
        var run = new Series(new(2025, 1, 7), SevenAm, FiftyMinutes, London, RecurrenceRule.Weekly([IsoDayOfWeek.Tuesday], everyWeeks: 2));

        LocalDate[] expected = [new(2026, 10, 13), new(2026, 10, 27)];
        Assert.Equal(expected, DatesOf(run, new(2026, 10, 5), new(2026, 10, 31)));
    }

    // 1 January 2025 to 1 October 2026 is 365 + 273 = 638 days; 638 = 3 x 212 + 2, so the 2nd (639) is the first
    // October date, then the 5th and 8th.
    [Fact]
    public void EveryFewDays_AskedForLongAfterItBegan_KeepsItsRhythm()
    {
        var stretch = new Series(new(2025, 1, 1), SevenAm, FiftyMinutes, London, RecurrenceRule.Daily(everyDays: 3));

        LocalDate[] expected = [new(2026, 10, 2), new(2026, 10, 5), new(2026, 10, 8)];
        Assert.Equal(expected, DatesOf(stretch, new(2026, 10, 1), new(2026, 10, 10)));
    }

    // Unlike iCalendar, the first date only counts if the rule includes it.
    [Fact]
    public void Weekly_StartingOnADayThatIsNotChosen_BeginsOnTheNextChosenDay()
    {
        var swim = new Series(new(2026, 10, 7), SevenAm, FiftyMinutes, London, RecurrenceRule.Weekly([IsoDayOfWeek.Monday]));

        LocalDate[] expected = [new(2026, 10, 12), new(2026, 10, 19), new(2026, 10, 26)];
        Assert.Equal(expected, DatesOf(swim, new(2026, 10, 1), new(2026, 10, 31)));
    }

    // Every two weeks on Sunday and Monday, from Sunday 4 October: whether Monday 5 October is in the first week
    // depends on which day the week starts.
    [Theory]
    [InlineData(IsoDayOfWeek.Monday, new[] { 4, 12, 18 })]
    [InlineData(IsoDayOfWeek.Sunday, new[] { 4, 5, 18, 19 })]
    public void EveryTwoWeeks_FollowsTheRulesWeekStart(IsoDayOfWeek weekStart, int[] expectedOctoberDays)
    {
        var series = new Series(new(2026, 10, 4), SevenAm, FiftyMinutes, London,
            RecurrenceRule.Weekly([IsoDayOfWeek.Sunday, IsoDayOfWeek.Monday], everyWeeks: 2, weekStart: weekStart));

        var expected = expectedOctoberDays.Select(day => new LocalDate(2026, 10, day));
        Assert.Equal(expected, DatesOf(series, new(2026, 10, 1), new(2026, 10, 19)));
    }

    // Series keep their clock time when the clocks change (requirements §8).
    [Fact]
    public void Occurrences_KeepTheirClockTime_WhenTheClocksGoBack()
    {
        var run = new Series(new(2026, 10, 20), SevenAm, FiftyMinutes, London, RecurrenceRule.Weekly([IsoDayOfWeek.Tuesday]));

        var occurrences = run.OccurrencesBetween(new(2026, 10, 20), new(2026, 10, 27));

        Assert.Equal(Instant.FromUtc(2026, 10, 20, 6, 0), occurrences[0].Time.Start); // 07:00 BST
        Assert.Equal(Instant.FromUtc(2026, 10, 27, 7, 0), occurrences[1].Time.Start); // 07:00 GMT
    }

    [Fact]
    public void EachOccurrence_LastsTheSeriesLength()
    {
        var gym = new Series(new(2026, 10, 5), SevenAm, FiftyMinutes, London, RecurrenceRule.Daily());

        var occurrence = Assert.Single(gym.OccurrencesBetween(new(2026, 10, 5), new(2026, 10, 5)));

        Assert.Equal(new Interval(Instant.FromUtc(2026, 10, 5, 6, 0), Instant.FromUtc(2026, 10, 5, 6, 50)), occurrence.Time);
    }

    // T4: on the night 01:00-01:59 happens twice, a 01:30 series occurs once, at the first 01:30.
    [Fact]
    public void ADailySeriesAt0130_OccursOnce_AtTheFirst0130_WhenTheClocksGoBack()
    {
        var series = new Series(new(2026, 10, 20), new(1, 30), FiftyMinutes, London, RecurrenceRule.Daily());

        var occurrence = Assert.Single(series.OccurrencesBetween(new(2026, 10, 25), new(2026, 10, 25)));

        Assert.Equal(Instant.FromUtc(2026, 10, 25, 0, 30), occurrence.Time.Start); // 01:30 BST
    }

    // T5: on the night 01:00-01:59 never happens, a 01:30 series moves forward by the gap, to 02:30.
    [Fact]
    public void ADailySeriesAt0130_OccursAt0230_WhenTheClocksGoForward()
    {
        var series = new Series(new(2027, 3, 20), new(1, 30), FiftyMinutes, London, RecurrenceRule.Daily());

        var occurrence = Assert.Single(series.OccurrencesBetween(new(2027, 3, 28), new(2027, 3, 28)));

        Assert.Equal(Instant.FromUtc(2027, 3, 28, 1, 30), occurrence.Time.Start); // 02:30 BST
    }

    // T6: changing timezone keeps the series' clock times ("gym at 7am wherever I am").
    [Fact]
    public void InAnotherZone_KeepsTheClockTime() // T6
    {
        var gym = new Series(new(2026, 10, 13), SevenAm, FiftyMinutes, London, RecurrenceRule.Daily());

        var gymInNewYork = gym.InZone(NewYork);

        Assert.Equal(Instant.FromUtc(2026, 10, 13, 11, 0), gymInNewYork.OccurrencesBetween(new(2026, 10, 13), new(2026, 10, 13))[0].Time.Start);
        Assert.Equal(Instant.FromUtc(2026, 10, 13, 6, 0), gym.OccurrencesBetween(new(2026, 10, 13), new(2026, 10, 13))[0].Time.Start);
    }

    private static IEnumerable<LocalDate> DatesOf(Series series, LocalDate from, LocalDate to) =>
        series.OccurrencesBetween(from, to).Select(occurrence => occurrence.Date);
}
