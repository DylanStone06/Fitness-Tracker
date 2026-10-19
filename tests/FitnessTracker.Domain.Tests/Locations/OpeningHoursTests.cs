using FitnessTracker.Domain.Footprints;
using FitnessTracker.Domain.Locations;
using NodaTime;

namespace FitnessTracker.Domain.Tests.Locations;

// TRV-1 and H4: a location's opening hours per weekday, closed days, and closing after midnight. Only the activity
// part of a footprint must be inside them (§7.1). Expected moments are written in UTC by hand: BST = UTC+1 until the
// clocks go back on Sunday 25 October 2026; GMT = UTC+0 until they go forward on Sunday 28 March 2027.
public class OpeningHoursTests
{
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];

    private static readonly OpeningHours Gym = new(new Dictionary<IsoDayOfWeek, OpenTimes>
    {
        [IsoDayOfWeek.Monday] = new(new LocalTime(6, 0), new LocalTime(22, 0)),
        [IsoDayOfWeek.Tuesday] = new(new LocalTime(6, 0), new LocalTime(22, 0)),
        [IsoDayOfWeek.Wednesday] = new(new LocalTime(6, 0), new LocalTime(22, 0)),
        // Thursday not listed: closed.
        [IsoDayOfWeek.Friday] = new(new LocalTime(18, 0), new LocalTime(2, 0)),
        [IsoDayOfWeek.Saturday] = new(new LocalTime(18, 0), new LocalTime(2, 0)),
    });

    private static Interval Utc(int year, int month, int day, int hour, int minute, int endDay, int endHour, int endMinute) =>
        new(Instant.FromUtc(year, month, day, hour, minute), Instant.FromUtc(year, month, endDay, endHour, endMinute));

    // T17: the gym opens at 06:00 and the journey there starts at 05:35. Valid: only the workout must be inside
    // the opening hours. Wednesday 7 October 2026, workout 06:00-06:50 BST = 05:00-05:50 UTC.
    [Fact]
    public void AWorkoutStartingAtOpeningTime_IsFine_EvenThoughTheJourneyStartsBeforeIt()
    {
        var workout = Utc(2026, 10, 7, 5, 0, 7, 5, 50);
        var footprint = Footprint.Around(workout, Buffers.None, new Journeys(Out: Duration.FromMinutes(25), Back: Duration.FromMinutes(25)));

        Assert.Equal(Instant.FromUtc(2026, 10, 7, 4, 35), footprint.Whole.Start);
        Assert.Null(Gym.ProblemWith(footprint.Activity, London));
        // ...whereas the whole footprint would not fit, which is why only the activity part is checked.
        Assert.NotNull(Gym.ProblemWith(footprint.Whole, London));
    }

    [Fact]
    public void AWorkoutStartingBeforeOpeningTime_IsAProblem_NamingThatDaysHours()
    {
        // Wednesday 7 October 2026, 05:50-06:40 BST.
        var problem = Gym.ProblemWith(Utc(2026, 10, 7, 4, 50, 7, 5, 40), London);

        Assert.Equal(new OpeningHoursProblem(new LocalDate(2026, 10, 7), new OpenTimes(new LocalTime(6, 0), new LocalTime(22, 0))), problem);
    }

    // Half-open: ending exactly at closing time is fine; one minute later isn't.
    [Fact]
    public void EndingAtClosingTime_IsFine_AndOneMinuteLaterIsNot()
    {
        // Wednesday 7 October 2026, 21:00-22:00 BST, then 21:01-22:01 BST.
        Assert.Null(Gym.ProblemWith(Utc(2026, 10, 7, 20, 0, 7, 21, 0), London));
        Assert.NotNull(Gym.ProblemWith(Utc(2026, 10, 7, 20, 1, 7, 21, 1), London));
    }

    [Fact]
    public void ADayThatIsNotListed_IsClosed()
    {
        // Thursday 8 October 2026, 10:00-11:00 BST.
        var problem = Gym.ProblemWith(Utc(2026, 10, 8, 9, 0, 8, 10, 0), London);

        Assert.Equal(new OpeningHoursProblem(new LocalDate(2026, 10, 8), Hours: null), problem);
    }

    // Friday's 18:00-02:00 runs into Saturday morning, so an activity early on Saturday is inside Friday's hours,
    // and one crossing midnight is inside them throughout.
    [Fact]
    public void ClosingAfterMidnight_CoversTheEarlyHoursOfTheNextDay()
    {
        // Friday 9 October 2026 23:30 to Saturday 00:45 BST; then Saturday 10 October 00:30-01:30 BST.
        Assert.Null(Gym.ProblemWith(Utc(2026, 10, 9, 22, 30, 9, 23, 45), London));
        Assert.Null(Gym.ProblemWith(Utc(2026, 10, 9, 23, 30, 10, 0, 30), London));
        // Saturday 01:30-02:30 BST runs past Friday's 02:00 closing.
        Assert.NotNull(Gym.ProblemWith(Utc(2026, 10, 10, 0, 30, 10, 1, 30), London));
    }

    // An activity that starts inside Friday's late opening but runs past its 02:00 close should be explained with
    // Friday's hours, not Saturday's own 18:00-02:00 (which would read as if 01:30 were inside them).
    [Fact]
    public void OverrunningAnOpeningThatStartedTheDayBefore_NamesThatDaysHours()
    {
        // Saturday 10 October 2026, 01:30-02:30 BST = 00:30-01:30 UTC.
        var problem = Gym.ProblemWith(Utc(2026, 10, 10, 0, 30, 10, 1, 30), London);

        Assert.Equal(new OpeningHoursProblem(new LocalDate(2026, 10, 9), new OpenTimes(new LocalTime(18, 0), new LocalTime(2, 0))), problem);
    }

    // Monday's 06:00-22:00 doesn't reach Tuesday's 06:00, so a span across that gap isn't covered.
    [Fact]
    public void AnActivityAcrossTheGapBetweenTwoDays_IsAProblem()
    {
        // Monday 5 October 2026 21:30 to Tuesday 06:30 BST.
        Assert.NotNull(Gym.ProblemWith(Utc(2026, 10, 5, 20, 30, 6, 5, 30), London));
    }

    [Fact]
    public void ALocationWithoutOpeningHours_IsAlwaysOpen()
    {
        // Thursday 8 October 2026, 03:00-04:00 BST.
        Assert.Null(OpeningHours.Always.ProblemWith(Utc(2026, 10, 8, 2, 0, 8, 3, 0), London));
    }

    // Saturday 24 October 2026 18:00 BST (17:00 UTC) to Sunday 02:00 GMT (02:00 UTC): 9 hours, because 01:00-01:59
    // happens twice. An activity in the second 01:00-01:59 (01:10-01:55 UTC) is inside; one ending at 02:30 GMT isn't.
    [Fact]
    public void OnTheNightTheClocksGoBack_ClosingAt0200_IsTheRealTwoOClock()
    {
        Assert.Null(Gym.ProblemWith(Utc(2026, 10, 25, 1, 10, 25, 1, 55), London));
        Assert.NotNull(Gym.ProblemWith(Utc(2026, 10, 25, 1, 30, 25, 2, 30), London));
    }

    // Saturday 27 March 2027 18:00 GMT (18:00 UTC) to Sunday 02:00 BST (01:00 UTC): 7 hours, because 01:00-01:59 is
    // skipped. 00:30-00:59 UTC is inside; 00:30-01:30 UTC (ending 02:30 BST) isn't.
    [Fact]
    public void OnTheNightTheClocksGoForward_ClosingAt0200_IsTheRealTwoOClock()
    {
        Assert.Null(Gym.ProblemWith(new Interval(Instant.FromUtc(2027, 3, 28, 0, 30), Instant.FromUtc(2027, 3, 28, 0, 59)), London));
        Assert.NotNull(Gym.ProblemWith(new Interval(Instant.FromUtc(2027, 3, 28, 0, 30), Instant.FromUtc(2027, 3, 28, 1, 30)), London));
    }

    // H4 is a hard rule, so on the nights the clocks change an opening is never longer than the clock shows. Going
    // forward (Sunday 28 March 2027, 01:00 GMT jumps to 02:00 BST), a place closing at 01:30 is shut from the jump:
    // 02:00 BST = 01:00 UTC. Saturday 23:30 GMT to Sunday 02:20 BST (23:30-01:20 UTC) runs past it.
    [Fact]
    public void OnTheNightTheClocksGoForward_AClosingTimeTheClocksSkip_IsWhenTheClockJumpsPastIt()
    {
        var lateGym = new OpeningHours(new Dictionary<IsoDayOfWeek, OpenTimes>
        {
            [IsoDayOfWeek.Saturday] = new(new LocalTime(18, 0), new LocalTime(1, 30)),
        });

        var problem = lateGym.ProblemWith(new Interval(Instant.FromUtc(2027, 3, 27, 23, 30), Instant.FromUtc(2027, 3, 28, 1, 20)), London);

        Assert.Equal(new OpeningHoursProblem(new LocalDate(2027, 3, 27), new OpenTimes(new LocalTime(18, 0), new LocalTime(1, 30))), problem);
    }

    // Going forward, a place opening at 01:30 is open from the jump, 02:00 BST (01:00 UTC): 02:00-02:30 BST fits.
    [Fact]
    public void OnTheNightTheClocksGoForward_AnOpeningTimeTheClocksSkip_IsWhenTheClockJumpsPastIt()
    {
        var earlyGym = new OpeningHours(new Dictionary<IsoDayOfWeek, OpenTimes>
        {
            [IsoDayOfWeek.Sunday] = new(new LocalTime(1, 30), new LocalTime(5, 0)),
        });

        Assert.Null(earlyGym.ProblemWith(new Interval(Instant.FromUtc(2027, 3, 28, 1, 0), Instant.FromUtc(2027, 3, 28, 1, 30)), London));
    }

    // Going back (Sunday 25 October 2026), 01:30 happens twice. Opening at 01:30 means the second one (GMT, 01:30
    // UTC) and closing at 01:30 the first (BST, 00:30 UTC), so the opening is never longer than the clock shows.
    [Fact]
    public void OnTheNightTheClocksGoBack_ARepeatedTime_OpensAtTheSecondAndClosesAtTheFirst()
    {
        var opensAt0130 = new OpeningHours(new Dictionary<IsoDayOfWeek, OpenTimes>
        {
            [IsoDayOfWeek.Sunday] = new(new LocalTime(1, 30), new LocalTime(5, 0)),
        });
        var closesAt0130 = new OpeningHours(new Dictionary<IsoDayOfWeek, OpenTimes>
        {
            [IsoDayOfWeek.Saturday] = new(new LocalTime(18, 0), new LocalTime(1, 30)),
        });

        // 01:30-02:00 BST (00:30-01:00 UTC) is before the second 01:30; 01:30-02:00 GMT (01:30-02:00 UTC) fits.
        Assert.NotNull(opensAt0130.ProblemWith(new Interval(Instant.FromUtc(2026, 10, 25, 0, 30), Instant.FromUtc(2026, 10, 25, 1, 0)), London));
        Assert.Null(opensAt0130.ProblemWith(new Interval(Instant.FromUtc(2026, 10, 25, 1, 30), Instant.FromUtc(2026, 10, 25, 2, 0)), London));
        // Saturday 23:00 BST to the second 01:00 (22:00 UTC to 01:00 UTC) runs past the first 01:30 (00:30 UTC).
        Assert.NotNull(closesAt0130.ProblemWith(new Interval(Instant.FromUtc(2026, 10, 24, 22, 0), Instant.FromUtc(2026, 10, 25, 1, 0)), London));
    }

    // Two openings that touch (Monday and Tuesday, each midnight to midnight) count as one, so an activity across
    // midnight between them fits: Monday 5 October 2026 23:30 to Tuesday 00:30 BST = 22:30-23:30 UTC.
    [Fact]
    public void AnActivityAcrossTwoTouchingOpenings_Fits()
    {
        var allDay = new OpeningHours(new Dictionary<IsoDayOfWeek, OpenTimes>
        {
            [IsoDayOfWeek.Monday] = new(LocalTime.Midnight, LocalTime.Midnight),
            [IsoDayOfWeek.Tuesday] = new(LocalTime.Midnight, LocalTime.Midnight),
        });

        Assert.Null(allDay.ProblemWith(new Interval(Instant.FromUtc(2026, 10, 5, 22, 30), Instant.FromUtc(2026, 10, 5, 23, 30)), London));
    }

    // Opening and closing at the same time means open until that time the next day: the whole day for midnight to
    // midnight, which is 25 hours on Sunday 25 October 2026 (23:00 UTC on the 24th to 00:00 UTC on the 26th).
    [Fact]
    public void OpeningAndClosingAtMidnight_OnTheDayTheClocksGoBack_LastsTheWholeDay()
    {
        var sundays = new OpeningHours(new Dictionary<IsoDayOfWeek, OpenTimes>
        {
            [IsoDayOfWeek.Sunday] = new(LocalTime.Midnight, LocalTime.Midnight),
        });

        // Sunday 23:00-23:59 GMT: more than 24 hours after opening, still Sunday.
        Assert.Null(sundays.ProblemWith(new Interval(Instant.FromUtc(2026, 10, 25, 23, 0), Instant.FromUtc(2026, 10, 25, 23, 59)), London));
        // Monday 26 October 00:00-00:30 GMT: closed.
        Assert.Equal(
            new OpeningHoursProblem(new LocalDate(2026, 10, 26), Hours: null),
            sundays.ProblemWith(new Interval(Instant.FromUtc(2026, 10, 26, 0, 0), Instant.FromUtc(2026, 10, 26, 0, 30)), London));
    }

    // Stored opening hours must be able to tell "no hours entered" (always open) from "closed every day".
    [Fact]
    public void AlwaysOpen_CanBeToldApartFromClosedEveryDay()
    {
        Assert.True(OpeningHours.Always.IsAlwaysOpen);
        Assert.False(new OpeningHours(new Dictionary<IsoDayOfWeek, OpenTimes>()).IsAlwaysOpen);
    }

    [Fact]
    public void ProblemWith_RefusesAnActivityWithNoStartOrNoEnd()
    {
        Assert.Throws<ArgumentException>(() => Gym.ProblemWith(new Interval(null, Instant.FromUtc(2026, 10, 7, 6, 0)), London));
        Assert.Throws<ArgumentException>(() => Gym.ProblemWith(new Interval(Instant.FromUtc(2026, 10, 7, 6, 0), null), London));
    }

    // Opening and closing at the same time means open until that time the next day.
    [Fact]
    public void OpeningAndClosingAtTheSameTime_IsOpenForADay()
    {
        var allDay = new OpeningHours(new Dictionary<IsoDayOfWeek, OpenTimes>
        {
            [IsoDayOfWeek.Thursday] = new(LocalTime.Midnight, LocalTime.Midnight),
        });

        // Thursday 8 October 2026 00:00-23:59 BST.
        Assert.Null(allDay.ProblemWith(Utc(2026, 10, 7, 23, 0, 8, 22, 59), London));
    }
}
