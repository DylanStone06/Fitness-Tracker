using FitnessTracker.Domain.Footprints;
using FitnessTracker.Domain.Time;
using NodaTime;

namespace FitnessTracker.Domain.Tests.Footprints;

// Requirements §7.1: footprint = preparation + travel out + activity + changing + travel back.
// Expected moments are written in UTC by hand. October 2026 is BST (UTC+1) until the clocks go back on Sunday 25th.
public class FootprintTests
{
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];

    // Wednesday 7 October 2026, 18:00-18:50 BST = 17:00-17:50 UTC.
    private static readonly Interval Workout = new(Instant.FromUtc(2026, 10, 7, 17, 0), Instant.FromUtc(2026, 10, 7, 17, 50));

    private static readonly Journeys GymJourneys = new(Out: Duration.FromMinutes(25), Back: Duration.FromMinutes(45));

    // T1: 25 min there, a 50 min workout and a 45 min walk home reserve exactly 120 min.
    [Fact]
    public void AGymSession_WithTravelBothWays_ReservesExactly120Minutes()
    {
        var footprint = Footprint.Around(Workout, Buffers.None, GymJourneys);

        Assert.Equal(Duration.FromMinutes(120), footprint.Whole.Duration);
        Assert.Equal(new Interval(Instant.FromUtc(2026, 10, 7, 16, 35), Instant.FromUtc(2026, 10, 7, 18, 35)), footprint.Whole);
        Assert.Equal(Workout, footprint.Activity);
    }

    // T1: with 10 min changing it reserves 130 min.
    [Fact]
    public void AGymSession_WithTenMinutesChanging_Reserves130Minutes()
    {
        var footprint = Footprint.Around(Workout, new Buffers(Preparation: Duration.Zero, Changing: Duration.FromMinutes(10)), GymJourneys);

        Assert.Equal(Duration.FromMinutes(130), footprint.Whole.Duration);
    }

    [Fact]
    public void Segments_RunInOrder_PreparationTravelOutActivityChangingTravelBack()
    {
        var buffers = new Buffers(Preparation: Duration.FromMinutes(15), Changing: Duration.FromMinutes(10));

        var footprint = Footprint.Around(Workout, buffers, GymJourneys);

        FootprintSegment[] expected =
        [
            new(FootprintPart.Preparation, new(Instant.FromUtc(2026, 10, 7, 16, 20), Instant.FromUtc(2026, 10, 7, 16, 35))),
            new(FootprintPart.TravelOut, new(Instant.FromUtc(2026, 10, 7, 16, 35), Instant.FromUtc(2026, 10, 7, 17, 0))),
            new(FootprintPart.Activity, Workout),
            new(FootprintPart.Changing, new(Instant.FromUtc(2026, 10, 7, 17, 50), Instant.FromUtc(2026, 10, 7, 18, 0))),
            new(FootprintPart.TravelBack, new(Instant.FromUtc(2026, 10, 7, 18, 0), Instant.FromUtc(2026, 10, 7, 18, 45))),
        ];
        Assert.Equal(expected, footprint.Segments);
        Assert.Equal(new Interval(Instant.FromUtc(2026, 10, 7, 16, 20), Instant.FromUtc(2026, 10, 7, 18, 45)), footprint.Whole);
    }

    // Travel applies only when the activity is away from base (§7.1); an activity at home has none.
    [Fact]
    public void AnActivityAtBase_HasNoTravelSegments_AndPartsOfZeroLengthAreLeftOut()
    {
        var footprint = Footprint.Around(Workout, new Buffers(Preparation: Duration.FromMinutes(5), Changing: Duration.Zero), Journeys.None);

        Assert.Equal([FootprintPart.Preparation, FootprintPart.Activity], footprint.Segments.Select(segment => segment.Part));
        Assert.Equal(Duration.FromMinutes(55), footprint.Whole.Duration);
    }

    // Requirements §8: lengths are elapsed time. Sunday 25 October 2026: a 00:30-01:30 activity (the first 01:30,
    // BST) is 23:30-00:30 UTC; travel 25 min there and 45 min back still reserves exactly 130 min across the hour
    // the clocks go back.
    [Fact]
    public void OnTheNightTheClocksGoBack_TheFootprintIsStillMeasuredInElapsedTime()
    {
        var activity = ClockTimes.ToInterval(
            new LocalDateTime(2026, 10, 25, 0, 30), new LocalDateTime(2026, 10, 25, 1, 30), London);

        var footprint = Footprint.Around(activity, Buffers.None, GymJourneys);

        Assert.Equal(new Interval(Instant.FromUtc(2026, 10, 24, 23, 5), Instant.FromUtc(2026, 10, 25, 1, 15)), footprint.Whole);
        Assert.Equal(Duration.FromMinutes(130), footprint.Whole.Duration);
    }

    // §8: Sunday 28 March 2027, the clocks go forward at 01:00 GMT. 00:30 GMT (00:30 UTC) to 02:30 BST (01:30 UTC) is
    // 60 min; with 25 min there and 45 min back the footprint is 00:05-02:15 UTC, exactly 130 min.
    [Fact]
    public void OnTheNightTheClocksGoForward_TheFootprintIsStillMeasuredInElapsedTime()
    {
        var activity = ClockTimes.ToInterval(
            new LocalDateTime(2027, 3, 28, 0, 30), new LocalDateTime(2027, 3, 28, 2, 30), London);

        var footprint = Footprint.Around(activity, Buffers.None, GymJourneys);

        Assert.Equal(new Interval(Instant.FromUtc(2027, 3, 28, 0, 5), Instant.FromUtc(2027, 3, 28, 2, 15)), footprint.Whole);
    }

    // H8: durations are positive (buffers and journeys may be zero, never negative); a footprint is at most 24 hours.
    // Each problem names its own part, so a form can show it next to the field that is wrong.
    [Fact]
    public void ProblemsWith_ReportsEveryBrokenLimit_NamingThePart()
    {
        var problems = Footprint.ProblemsWith(
            activityLength: Duration.Zero,
            new Buffers(Preparation: Duration.FromMinutes(-5), Changing: Duration.Zero),
            new Journeys(Out: Duration.Zero, Back: Duration.FromMinutes(-1)));

        Assert.Equal(
            [FootprintProblem.ActivityNotLongerThanZero, FootprintProblem.NegativePreparation, FootprintProblem.NegativeTravelBack],
            problems);
    }

    [Fact]
    public void ProblemsWith_SaysWhichBufferAndWhichJourneyIsNegative()
    {
        var problems = Footprint.ProblemsWith(
            Duration.FromMinutes(50),
            new Buffers(Preparation: Duration.FromMinutes(10), Changing: Duration.FromMinutes(-5)),
            new Journeys(Out: Duration.FromMinutes(-1), Back: Duration.FromMinutes(45)));

        Assert.Equal([FootprintProblem.NegativeChanging, FootprintProblem.NegativeTravelOut], problems);
    }

    // A negative value mustn't hide a footprint that is too long anyway: the shortest valid one is already 25 hours.
    [Fact]
    public void ProblemsWith_ANegativePart_DoesNotHideATotalOver24Hours()
    {
        var problems = Footprint.ProblemsWith(
            Duration.FromHours(25), new Buffers(Preparation: Duration.FromHours(-2), Changing: Duration.Zero), Journeys.None);

        Assert.Equal([FootprintProblem.NegativePreparation, FootprintProblem.LongerThanADay], problems);
    }

    [Fact]
    public void ProblemsWith_AllowsExactly24Hours_AndRefusesOneMinuteMore()
    {
        var oneHourEachWay = new Journeys(Out: Duration.FromHours(1), Back: Duration.FromHours(1));

        Assert.Empty(Footprint.ProblemsWith(Duration.FromHours(22), Buffers.None, oneHourEachWay));
        Assert.Equal(
            [FootprintProblem.LongerThanADay],
            Footprint.ProblemsWith(Duration.FromHours(22) + Duration.FromMinutes(1), Buffers.None, oneHourEachWay));
    }

    [Fact]
    public void ProblemsWith_CountsPreparationAndChanging_TowardsThe24Hours()
    {
        var oneHourEach = new Buffers(Preparation: Duration.FromHours(1), Changing: Duration.FromHours(1));
        var oneMinuteMore = new Buffers(Preparation: Duration.FromHours(1), Changing: Duration.FromHours(1) + Duration.FromMinutes(1));

        Assert.Empty(Footprint.ProblemsWith(Duration.FromHours(22), oneHourEach, Journeys.None));
        Assert.Equal([FootprintProblem.LongerThanADay], Footprint.ProblemsWith(Duration.FromHours(22), oneMinuteMore, Journeys.None));
    }

    // Q7 (owner's decision): H8's limit is 24 hours, or the whole day on a day longer than that. Midnight to midnight
    // on the day the clocks go back lasts 25 hours (23:00 UTC 24 Oct to 00:00 UTC 26 Oct 2026) and is allowed on that
    // day, but 25 hours is still too long on an ordinary day.
    [Fact]
    public void AnAllDayActivityOnTheDayTheClocksGoBack_IsAllowed_OnThatDayOnly()
    {
        var wholeDay = ClockTimes.ToInterval(
            new LocalDateTime(2026, 10, 25, 0, 0), new LocalDateTime(2026, 10, 26, 0, 0), London);

        Assert.Equal(Duration.FromHours(25), wholeDay.Duration);
        Assert.Empty(Footprint.ProblemsWith(wholeDay.Duration, Buffers.None, Journeys.None, Footprint.LongestOn(new LocalDate(2026, 10, 25), London)));
        Assert.Equal([FootprintProblem.LongerThanADay], Footprint.ProblemsWith(wholeDay.Duration, Buffers.None, Journeys.None));
    }

    // The limit on a day: 25 hours on the day the clocks go back, but never less than 24, so the 23-hour day the clocks
    // go forward still allows 24 (nothing allowed before Q7 is refused now).
    [Theory]
    [InlineData(2026, 10, 25, 25)]
    [InlineData(2027, 3, 28, 24)]
    [InlineData(2026, 11, 2, 24)]
    public void LongestOn_IsTheDaysLength_ButAtLeast24Hours(int year, int month, int day, int hours)
    {
        Assert.Equal(Duration.FromHours(hours), Footprint.LongestOn(new LocalDate(year, month, day), London));
    }

    // Samoa skipped Friday 30 December 2011 entirely. The days next to it, and the skipped day itself, get 24 hours
    // rather than an error (the form accepts dates back to 1900, and Pacific/Apia is in the timezone list).
    [Theory]
    [InlineData(29)]
    [InlineData(30)]
    [InlineData(31)]
    public void LongestOn_NextToADayTheClocksSkipped_IsTwentyFourHours(int december)
    {
        var apia = DateTimeZoneProviders.Tzdb["Pacific/Apia"];

        Assert.Equal(Duration.FromHours(24), Footprint.LongestOn(new LocalDate(2011, 12, december), apia));
    }

    // The error names the argument that is wrong, not always the activity.
    [Fact]
    public void Around_NamesTheArgumentThatIsWrong()
    {
        var instant = Instant.FromUtc(2026, 10, 7, 17, 0);

        Assert.Equal("activity", Assert.Throws<ArgumentException>(
            () => Footprint.Around(new Interval(instant, instant), Buffers.None, Journeys.None)).ParamName);
        Assert.Equal("buffers", Assert.Throws<ArgumentException>(
            () => Footprint.Around(Workout, new Buffers(Duration.Zero, Duration.FromMinutes(-5)), Journeys.None)).ParamName);
        Assert.Equal("journeys", Assert.Throws<ArgumentException>(
            () => Footprint.Around(Workout, Buffers.None, new Journeys(Duration.Zero, Duration.FromMinutes(-1)))).ParamName);
    }

    [Fact]
    public void Around_RefusesAnActivityWithNoStartOrNoEnd()
    {
        var start = Instant.FromUtc(2026, 10, 7, 17, 0);

        Assert.Throws<ArgumentException>(() => Footprint.Around(new Interval(start, null), Buffers.None, Journeys.None));
        Assert.Throws<ArgumentException>(() => Footprint.Around(new Interval(null, start), Buffers.None, Journeys.None));
    }

    [Fact]
    public void Around_RefusesAFootprintWithProblems_SayingWhy()
    {
        var instant = Instant.FromUtc(2026, 10, 7, 17, 0);

        var error = Assert.Throws<ArgumentException>(() => Footprint.Around(new Interval(instant, instant), Buffers.None, Journeys.None));

        Assert.Contains(Footprint.Describe(FootprintProblem.ActivityNotLongerThanZero), error.Message);
    }
}
