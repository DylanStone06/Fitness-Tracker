using FitnessTracker.Domain.Footprints;
using FitnessTracker.Domain.Locations;
using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Domain.Time;
using NodaTime;

namespace FitnessTracker.Domain.Tests.Scheduling;

// SCH-1, first part: the one check every new or changed activity goes through before it is saved. Errors (travel
// that can't be worked out, a repeat that never happens, H4 opening hours, H8) stop the save; clashes (H1) need the
// user to confirm (Q2). Today is Monday 19 October 2026 throughout.
public class ActivityCheckTests
{
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly LocalDate Today = new(2026, 10, 19);
    private static readonly Guid Home = Guid.Parse("0199e3a0-0000-7000-8000-000000000001");
    private static readonly Guid Gym = Guid.Parse("0199e3a0-0000-7000-8000-000000000002");

    [Fact]
    public void AnActivityWithNothingElseAround_HasNoProblems()
    {
        var result = ActivityCheck.Check(GymSession(new LocalDate(2026, 11, 2)), [], Context());

        Assert.Empty(result.Problems);
        Assert.Empty(result.Clashes);
    }

    // T1 and T10: gym 18:00-18:50 with 10 minutes changing and a 45-minute walk home reserves until 19:45, so dinner at
    // home from 19:30 clashes with the journey back, not with the workout.
    [Fact]
    public void AClash_IsFoundOnFootprints_NamingThePartsThatOverlap()
    {
        var dinner = OneOff("Dinner", new LocalDate(2026, 11, 2), new LocalTime(19, 30), minutes: 60, at: Home);

        var result = ActivityCheck.Check(GymSession(new LocalDate(2026, 11, 2)), [dinner], Context());

        var clash = Assert.Single(result.Clashes);
        Assert.Equal((FootprintPart.TravelBack, FootprintPart.Activity), (clash.CandidatePart, clash.OtherPart));
        Assert.Equal("On Mon 2 Nov 2026, its journey back overlaps Dinner (Dinner is 19:30 to 20:30).", ActivityCheck.Describe(clash, London));
    }

    // T11: footprints that only touch don't clash (half-open intervals).
    [Fact]
    public void BackToBackFootprints_DontClash()
    {
        var dinner = OneOff("Dinner", new LocalDate(2026, 11, 2), new LocalTime(19, 45), minutes: 60, at: Home);

        Assert.Empty(ActivityCheck.Check(GymSession(new LocalDate(2026, 11, 2)), [dinner], Context()).Clashes);
    }

    // An activity being edited is among the stored ones; it mustn't clash with its own old version.
    [Fact]
    public void AnActivity_DoesntClashWithItsOwnStoredVersion()
    {
        var gym = GymSession(new LocalDate(2026, 11, 2));

        Assert.Empty(ActivityCheck.Check(gym, [gym with { Title = "Gym (before the edit)" }], Context()).Clashes);
    }

    // A repeating activity is checked for 8 weeks from its first date: Mondays from 2 November reach 21 December, not
    // 4 January.
    [Theory]
    [InlineData(2026, 12, 21, true)]
    [InlineData(2027, 1, 4, false)]
    public void ARepeatingActivity_IsCheckedForEightWeeks(int year, int month, int day, bool clashes)
    {
        var weekly = Weekly(new LocalDate(2026, 11, 2), [IsoDayOfWeek.Monday], new LocalTime(18, 0));
        var party = OneOff("Party", new LocalDate(year, month, day), new LocalTime(18, 0), minutes: 120, at: Home);

        var result = ActivityCheck.Check(weekly, [party], Context());

        Assert.Equal(clashes, result.Clashes.Count == 1);
        Assert.Equal(new DateInterval(new LocalDate(2026, 11, 2), new LocalDate(2026, 12, 27)), result.Checked);
    }

    // One that started in the past is checked from today: a clash last week is history, not a problem to fix.
    [Theory]
    [InlineData(12, false)]
    [InlineData(26, true)]
    public void ARepeatingActivityThatStartedEarlier_IsCheckedFromToday(int octoberDay, bool clashes)
    {
        var weekly = Weekly(new LocalDate(2026, 10, 5), [IsoDayOfWeek.Monday], new LocalTime(18, 0));
        var party = OneOff("Party", new LocalDate(2026, 10, octoberDay), new LocalTime(18, 0), minutes: 120, at: Home);

        var result = ActivityCheck.Check(weekly, [party], Context());

        Assert.Equal(clashes, result.Clashes.Count == 1);
        Assert.Equal(Today, result.Checked.Start);
    }

    // An activity entirely in the past (logged afterwards, say) is still checked, on its own dates: the window from
    // today would have nothing in it, and a clash saved without "Save anyway" would look like an accepted one.
    [Fact]
    public void AnActivityEntirelyInThePast_IsCheckedOnItsOwnDates()
    {
        var meeting = OneOff("Team meeting", new LocalDate(2026, 10, 12), new LocalTime(7, 0), minutes: 60, at: Home);
        var gym = OneOff("Gym", new LocalDate(2026, 10, 12), new LocalTime(7, 30), minutes: 50, at: Home);

        var result = ActivityCheck.Check(gym, [meeting], Context());

        Assert.Equal("Team meeting", Assert.Single(result.Clashes).Other.Label);
        Assert.Equal(new LocalDate(2026, 10, 12), result.Checked.Start);
    }

    // SCH-1, CAL-4: a moved occurrence is checked wherever it lands, not only on the dates a series is checked on. Here the
    // run starts on Thursday 22 October, so the series is checked from then, and its first one is moved to Tuesday 20th,
    // into a call. (Found in the browser: the move was saved without the clash being shown.)
    [Fact]
    public void AnOccurrenceMovedBeforeTheDatesChecked_IsStillCheckedForClashes()
    {
        var run = MovedRun(new LocalDateTime(2026, 10, 20, 18, 20), minutes: 40);
        var call = OneOff("Call", new LocalDate(2026, 10, 20), new LocalTime(18, 30), minutes: 20, at: null);

        var clash = Assert.Single(ActivityCheck.Check(run, [call], Context()).Clashes);

        Assert.Equal("Call", clash.Other.Label);
        Assert.True(ActivityCheck.OthersThatCanClash(run.Series, Today).Contains(new LocalDate(2026, 10, 20)));
    }

    // ... and beyond the 8 weeks (to Thursday 4 March 2027, 19 weeks on).
    [Fact]
    public void AnOccurrenceMovedBeyondTheWeeksChecked_IsStillCheckedForClashes()
    {
        var run = MovedRun(new LocalDateTime(2027, 3, 4, 18, 20), minutes: 40);
        var call = OneOff("Call", new LocalDate(2027, 3, 4), new LocalTime(18, 30), minutes: 20, at: null);

        Assert.Single(ActivityCheck.Check(run, [call], Context()).Clashes);
        Assert.True(ActivityCheck.OthersThatCanClash(run.Series, Today).Contains(new LocalDate(2027, 3, 4)));
    }

    // H8 too: moved two years on with a length of 25 hours, on an ordinary day.
    [Fact]
    public void AnOccurrenceMovedBeyondTheYearChecked_IsStillCheckedForItsLength()
    {
        var run = MovedRun(new LocalDateTime(2028, 6, 1, 6, 0), minutes: 25 * 60);

        var problem = Assert.Single(ActivityCheck.Check(run, [], Context()).Problems);

        Assert.Equal(CheckAspect.Length, problem.Aspect);
    }

    // But one moved into the past is history, like a clash last week (above): Mondays from 5 October, the 12 October
    // session moved to Tuesday 13th for 25 hours, into a call. Editing the series today isn't refused for that day's
    // length, or asked about its clash. (Phase 5 code review.)
    [Fact]
    public void AnOccurrenceMovedIntoThePast_IsHistory_NotChecked()
    {
        var weekly = Weekly(new LocalDate(2026, 10, 5), [IsoDayOfWeek.Monday], new LocalTime(18, 0));
        var moved = weekly with
        {
            Series = weekly.Series.MoveOccurrence(new LocalDate(2026, 10, 12), new LocalDateTime(2026, 10, 13, 18, 0), Duration.FromHours(25)),
        };
        var call = OneOff("Call", new LocalDate(2026, 10, 13), new LocalTime(18, 30), minutes: 20, at: null);

        var result = ActivityCheck.Check(moved, [call], Context());

        Assert.Empty(result.Problems);
        Assert.Empty(result.Clashes);
        Assert.False(ActivityCheck.OthersThatCanClash(moved.Series, Today).Contains(new LocalDate(2026, 10, 13)));
    }

    // A series entirely in the past is checked on its own dates (above), so its moved ones are too.
    [Fact]
    public void AnOccurrenceMovedWithinASeriesEntirelyInThePast_IsChecked()
    {
        var weekly = Weekly(new LocalDate(2026, 9, 7), [IsoDayOfWeek.Monday], new LocalTime(18, 0));
        var ended = weekly with
        {
            Series = new Series(weekly.Series.FirstDate, weekly.Series.StartTime, weekly.Series.Length, London, RecurrenceRule.Weekly([IsoDayOfWeek.Monday]).EndingAfter(2))
                .MoveOccurrence(new LocalDate(2026, 9, 14), new LocalDateTime(2026, 9, 1, 18, 0), Duration.FromMinutes(50)),
        };
        var call = OneOff("Call", new LocalDate(2026, 9, 1), new LocalTime(18, 30), minutes: 20, at: null);

        Assert.Equal("Call", Assert.Single(ActivityCheck.Check(ended, [call], Context()).Clashes).Other.Label);
        Assert.True(ActivityCheck.OthersThatCanClash(ended.Series, Today).Contains(new LocalDate(2026, 9, 1)));
    }

    // A series that next happens more than a year away isn't over: Mondays every 60 weeks from Monday 5 October next
    // fall on 29 November 2027. Its 5 October session, moved to Sunday 4th for 25 hours, is history. (Scheduling
    // review M-1: "nothing in the year looked at" was taken to mean "entirely in the past".)
    [Fact]
    public void AnOccurrenceMovedIntoThePast_OfASeriesNextHappeningOverAYearAway_IsHistory()
    {
        var series = new Series(new LocalDate(2026, 10, 5), new LocalTime(18, 0), Duration.FromMinutes(50), London, RecurrenceRule.Weekly([IsoDayOfWeek.Monday], everyWeeks: 60))
            .MoveOccurrence(new LocalDate(2026, 10, 5), new LocalDateTime(2026, 10, 4, 18, 0), Duration.FromHours(25));

        Assert.Empty(ActivityCheck.Check(AtHome(series), [], Context()).Problems);
    }

    // ... but one moved to start yesterday and still running today is checked, as the home page shows its clash: Work
    // daily at 07:30 for 8 hours, Sunday 18 October's moved to 22:00 for 10 hours, runs into today's from 07:30 to
    // 08:00. (Scheduling review L-1.)
    [Fact]
    public void AMovedOccurrenceStillRunningToday_ClashesWithTodaysOccurrence()
    {
        var series = new Series(new LocalDate(2026, 10, 5), new LocalTime(7, 30), Duration.FromHours(8), London, RecurrenceRule.Daily())
            .MoveOccurrence(new LocalDate(2026, 10, 18), new LocalDateTime(2026, 10, 18, 22, 0), Duration.FromHours(10));
        var work = new PlannedActivity(Guid.NewGuid(), "Work", series, Buffers.None, null);

        Assert.Equal(["Work"], Assert.Single(Agenda.Between([work], Context(), Today, Today)).ClashesWith);
        var clash = Assert.Single(ActivityCheck.Check(work, [], Context()).Clashes);
        Assert.Equal(new Interval(Instant.FromUtc(2026, 10, 19, 6, 30), Instant.FromUtc(2026, 10, 19, 7, 0)), clash.Overlap);
    }

    // Today counts as from today: the first run moved onto Monday 19th 18:20, into a call. (Scheduling review L-3.)
    [Fact]
    public void AnOccurrenceMovedOntoToday_IsChecked()
    {
        var run = MovedRun(new LocalDateTime(2026, 10, 19, 18, 20), minutes: 40);
        var call = OneOff("Call", new LocalDate(2026, 10, 19), new LocalTime(18, 30), minutes: 20, at: null);

        Assert.Equal("Call", Assert.Single(ActivityCheck.Check(run, [call], Context()).Clashes).Other.Label);
    }

    // CAL-4, H1: a change to one occurrence is checked on that occurrence, where it now is. Gym on Mondays and Wednesdays
    // 07:00-08:00 at home; Wednesday 4 November moved to Monday 2nd 08:00-09:00 runs into breakfast (07:30-08:30).
    // Monday's own session ends as it starts (half-open): no clash. (Scheduling review H-2: the old way compared
    // descriptions, and this clash reads like breakfast's existing one with Monday's session.)
    [Fact]
    public void CheckOccurrence_FindsAClashWhereTheOccurrenceNowIs()
    {
        var gym = GymMondaysAndWednesdays().MoveOccurrence(new LocalDate(2026, 11, 4), new LocalDateTime(2026, 11, 2, 8, 0), Duration.FromHours(1));
        var breakfast = OneOff("Breakfast", new LocalDate(2026, 11, 2), new LocalTime(7, 30), minutes: 60, at: null);

        var result = ActivityCheck.CheckOccurrence(AtHome(gym), new LocalDate(2026, 11, 4), [breakfast], Context());

        var clash = Assert.Single(result.Clashes);
        Assert.Equal("Breakfast", clash.Other.Label);
        Assert.Equal(new Interval(Instant.FromUtc(2026, 11, 2, 8, 0), Instant.FromUtc(2026, 11, 2, 8, 30)), clash.Overlap);
    }

    // ... and only there: breakfast clashing with Monday's session isn't this change's doing.
    [Fact]
    public void CheckOccurrence_LeavesTheRestOfTheSeriesAlone()
    {
        var gym = GymMondaysAndWednesdays().MoveOccurrence(new LocalDate(2026, 11, 4), new LocalDateTime(2026, 11, 4, 9, 0), Duration.FromHours(1));
        var breakfast = OneOff("Breakfast", new LocalDate(2026, 11, 2), new LocalTime(7, 30), minutes: 60, at: null);

        var result = ActivityCheck.CheckOccurrence(AtHome(gym), new LocalDate(2026, 11, 4), [breakfast], Context());

        Assert.Empty(result.Problems);
        Assert.Empty(result.Clashes);
    }

    // Its own series counts too: moved onto Monday 07:30, it overlaps Monday's own session.
    [Fact]
    public void CheckOccurrence_FindsAClashWithItsOwnSeries()
    {
        var gym = GymMondaysAndWednesdays().MoveOccurrence(new LocalDate(2026, 11, 4), new LocalDateTime(2026, 11, 2, 7, 30), Duration.FromHours(1));

        var clash = Assert.Single(ActivityCheck.CheckOccurrence(AtHome(gym), new LocalDate(2026, 11, 4), [], Context()).Clashes);

        Assert.Equal(new Interval(Instant.FromUtc(2026, 11, 2, 7, 30), Instant.FromUtc(2026, 11, 2, 8, 0)), clash.Overlap);
    }

    // A cancelled occurrence takes no time: nothing to check, even where the series has problems (review L-5: it no
    // longer brings back old clashes either).
    [Fact]
    public void CheckOccurrence_OfACancelledOne_HasNothingToCheck()
    {
        var gym = GymMondaysAndWednesdays().CancelOccurrence(new LocalDate(2026, 11, 4));
        var atTheGym = new PlannedActivity(Guid.NewGuid(), "Gym", gym, Buffers.None, Gym);

        var result = ActivityCheck.CheckOccurrence(atTheGym, new LocalDate(2026, 11, 4), [], Context(withHome: false));

        Assert.Empty(result.Problems);
        Assert.Empty(result.Clashes);
    }

    // Review H-1: travel that can't be worked out (a travel time deleted since) is a problem for the moved occurrence,
    // never a reason to skip its clash check.
    [Fact]
    public void CheckOccurrence_WhoseTravelIsUnknown_IsAProblem()
    {
        var gym = GymMondaysAndWednesdays().MoveOccurrence(new LocalDate(2026, 11, 4), new LocalDateTime(2026, 11, 5, 18, 0), Duration.FromMinutes(50));
        var atTheGym = new PlannedActivity(Guid.NewGuid(), "Gym", gym, Buffers.None, Gym);

        var problem = Assert.Single(ActivityCheck.CheckOccurrence(atTheGym, new LocalDate(2026, 11, 4), [], Context(withHome: false)).Problems);

        Assert.Equal(CheckAspect.Location, problem.Aspect);
    }

    // H4 on the occurrence where it now is: Sunday, when the gym is closed. (Review H-1: matched by its text, the old way
    // could mistake this for another date's problem.)
    [Fact]
    public void CheckOccurrence_OutsideOpeningHours_IsAProblem()
    {
        var weekdays = new OpeningHours(Enum.GetValues<IsoDayOfWeek>()
            .Where(day => day is not (IsoDayOfWeek.None or IsoDayOfWeek.Sunday))
            .ToDictionary(day => day, _ => new OpenTimes(new LocalTime(6, 0), new LocalTime(22, 0))));
        var gym = GymMondaysAndWednesdays().MoveOccurrence(new LocalDate(2026, 11, 4), new LocalDateTime(2026, 11, 8, 10, 0), Duration.FromMinutes(50));
        var atTheGym = new PlannedActivity(Guid.NewGuid(), "Gym", gym, Buffers.None, Gym);

        var problem = Assert.Single(ActivityCheck.CheckOccurrence(atTheGym, new LocalDate(2026, 11, 4), [], Context(gymHours: weekdays)).Problems);

        Assert.Contains("Town gym is closed on Sun 8 Nov 2026", problem.Message);
    }

    // The other activities a moved occurrence can clash with are those near where it now is.
    [Fact]
    public void OthersThatCanClashWithAnOccurrence_AreNearWhereItNowIs()
    {
        var gym = GymMondaysAndWednesdays().MoveOccurrence(new LocalDate(2026, 11, 4), new LocalDateTime(2027, 3, 4, 8, 0), Duration.FromHours(1));

        var dates = ActivityCheck.OthersThatCanClashWith(gym, new LocalDate(2026, 11, 4));

        Assert.Equal(new DateInterval(new LocalDate(2027, 3, 1), new LocalDate(2027, 3, 7)), dates);
    }

    // Gym on Mondays and Wednesdays at 07:00 for an hour, from Monday 2 November 2026.
    private static Series GymMondaysAndWednesdays() =>
        new(new LocalDate(2026, 11, 2), new LocalTime(7, 0), Duration.FromHours(1), London, RecurrenceRule.Weekly([IsoDayOfWeek.Monday, IsoDayOfWeek.Wednesday]));

    private static PlannedActivity AtHome(Series series) => new(Guid.NewGuid(), "Gym", series, Buffers.None, null);

    // A run on Thursdays at 07:00 for 40 minutes from Thursday 22 October 2026, at home, its first one moved.
    private static PlannedActivity MovedRun(LocalDateTime movedTo, int minutes) =>
        new(Guid.NewGuid(), "Run",
            new Series(new LocalDate(2026, 10, 22), new LocalTime(7, 0), Duration.FromMinutes(40), London, RecurrenceRule.Weekly([IsoDayOfWeek.Thursday]))
                .MoveOccurrence(new LocalDate(2026, 10, 22), movedTo, Duration.FromMinutes(minutes)),
            Buffers.None, null);

    // ACC-3: with the 12-hour clock chosen, the check's explanations use it too.
    [Fact]
    public void WithTheTwelveHourClock_ExplanationsReadThatWay()
    {
        var hours = Daily(new OpenTimes(new LocalTime(6, 0), new LocalTime(22, 0)));
        var early = GymSession(new LocalDate(2026, 11, 2), new LocalTime(5, 30));
        var dinner = OneOff("Dinner", new LocalDate(2026, 11, 2), new LocalTime(19, 30), minutes: 60, at: Home);
        var twelveHour = Context(gymHours: hours) with { Clock = ClockFormat.TwelveHour };

        var problem = Assert.Single(ActivityCheck.Check(early, [], twelveHour).Problems);
        var clash = Assert.Single(ActivityCheck.Check(GymSession(new LocalDate(2026, 11, 2)), [dinner], Context() with { Clock = ClockFormat.TwelveHour }).Clashes);

        Assert.Contains("Town gym is open 6:00am to 10:00pm on Mon 2 Nov 2026", problem.Message);
        Assert.Equal("On Mon 2 Nov 2026, its journey back overlaps Dinner (Dinner is 7:30pm to 8:30pm).", ActivityCheck.Describe(clash, London, ClockFormat.TwelveHour));
    }

    // H4: the activity must be inside the opening hours, and the problem says when the place is open.
    [Fact]
    public void AnActivityBeforeOpeningTime_IsAProblemWithItsTime()
    {
        var hours = Daily(new OpenTimes(new LocalTime(6, 0), new LocalTime(22, 0)));
        var early = GymSession(new LocalDate(2026, 11, 2), new LocalTime(5, 30));

        var problem = Assert.Single(ActivityCheck.Check(early, [], Context(gymHours: hours)).Problems);

        Assert.Equal(CheckAspect.Time, problem.Aspect);
        Assert.Contains("Town gym is open 06:00 to 22:00 on Mon 2 Nov 2026", problem.Message);
    }

    // T17: only the workout has to be inside the opening hours; travel can start at 05:35.
    [Fact]
    public void AWorkoutStartingAtOpeningTime_WithTravelBeforeIt_IsFine()
    {
        var hours = Daily(new OpenTimes(new LocalTime(6, 0), new LocalTime(22, 0)));

        Assert.Empty(ActivityCheck.Check(GymSession(new LocalDate(2026, 11, 2), new LocalTime(6, 0)), [], Context(gymHours: hours)).Problems);
    }

    // H4 depends on the day, so every occurrence is checked: Saturdays and Sundays at a gym closed on Sundays fail on
    // each of the 8 Sundays from 8 November; the problem names the first and counts the rest.
    [Fact]
    public void OpeningHours_AreCheckedForEveryOccurrence()
    {
        var weekdaysAndSaturday = new Dictionary<IsoDayOfWeek, OpenTimes>();
        foreach (var day in new[] { IsoDayOfWeek.Monday, IsoDayOfWeek.Tuesday, IsoDayOfWeek.Wednesday, IsoDayOfWeek.Thursday, IsoDayOfWeek.Friday, IsoDayOfWeek.Saturday })
        {
            weekdaysAndSaturday[day] = new OpenTimes(new LocalTime(6, 0), new LocalTime(22, 0));
        }

        var weekend = Weekly(new LocalDate(2026, 11, 7), [IsoDayOfWeek.Saturday, IsoDayOfWeek.Sunday], new LocalTime(10, 0), at: Gym);

        var problem = Assert.Single(ActivityCheck.Check(weekend, [], Context(gymHours: new OpeningHours(weekdaysAndSaturday))).Problems);

        // Opening hours are checked for a year (53 weeks to 12 Nov 2027): 53 Sundays, the first and 52 more.
        Assert.Contains("Town gym is closed on Sun 8 Nov 2026", problem.Message);
        Assert.Contains("52 more", problem.Message);
    }

    // Moved occurrences are named in date order with the rest, and counted up to the last date checked: Mondays from
    // 2 November at a gym closed on Sundays, the first three moved to Sundays: 1 November (before it starts), 8 November,
    // and 4 June 2028 (after the year checked). (Scheduling review, Phase 5: the first named was 8 November, and the
    // count said "up to Sun 7 Nov 2027".)
    [Fact]
    public void MovedOccurrencesOutsideOpeningHours_AreNamedInDateOrder_UpToTheLastChecked()
    {
        var exceptSunday = new OpeningHours(Enum.GetValues<IsoDayOfWeek>()
            .Where(day => day is not (IsoDayOfWeek.None or IsoDayOfWeek.Sunday))
            .ToDictionary(day => day, _ => new OpenTimes(new LocalTime(6, 0), new LocalTime(22, 0))));
        var mondays = Weekly(new LocalDate(2026, 11, 2), [IsoDayOfWeek.Monday], new LocalTime(10, 0), at: Gym);
        var gym = mondays with
        {
            Series = mondays.Series
                .MoveOccurrence(new LocalDate(2026, 11, 2), new LocalDateTime(2026, 11, 1, 10, 0), Duration.FromMinutes(50))
                .MoveOccurrence(new LocalDate(2026, 11, 9), new LocalDateTime(2026, 11, 8, 10, 0), Duration.FromMinutes(50))
                .MoveOccurrence(new LocalDate(2026, 11, 16), new LocalDateTime(2028, 6, 4, 10, 0), Duration.FromMinutes(50)),
        };

        var problem = Assert.Single(ActivityCheck.Check(gym, [], Context(gymHours: exceptSunday)).Problems);

        Assert.Contains("Town gym is closed on Sun 1 Nov 2026", problem.Message);
        Assert.Contains("2 more dates up to Sun 4 Jun 2028", problem.Message);
    }

    // H4 can't be broken by a date the 8 weeks of clash checking don't reach (scheduling review): every 10 days from
    // Monday 2 November first lands on a Friday on 1 January 2027, when this gym is closed.
    [Fact]
    public void ADailyRuleEveryTenDays_IsCheckedOnEveryWeekdayItFallsOn()
    {
        var exceptFriday = new OpeningHours(Enum.GetValues<IsoDayOfWeek>()
            .Where(day => day is not (IsoDayOfWeek.None or IsoDayOfWeek.Friday))
            .ToDictionary(day => day, _ => new OpenTimes(new LocalTime(6, 0), new LocalTime(22, 0))));
        var everyTenDays = new PlannedActivity(Guid.NewGuid(), "Gym",
            new Series(new LocalDate(2026, 11, 2), new LocalTime(18, 0), Duration.FromMinutes(50), London, RecurrenceRule.Daily(10)),
            new Buffers(Duration.Zero, Duration.FromMinutes(10)), Gym);

        var problem = Assert.Single(ActivityCheck.Check(everyTenDays, [], Context(gymHours: exceptFriday)).Problems);

        Assert.Contains("Town gym is closed on Fri 1 Jan 2027", problem.Message);
    }

    // T5 with opening hours: a daily 01:30 swim at a place open 18:00-02:00 is fine until Sunday 28 March 2027, when
    // 01:30 becomes 02:30 (§8) and the place closes as the clocks reach 02:00.
    [Fact]
    public void ALateActivity_PushedPastClosingWhenTheClocksGoForward_IsAProblem()
    {
        var late = new PlannedActivity(Guid.NewGuid(), "Late swim",
            new Series(new LocalDate(2026, 11, 2), new LocalTime(1, 30), Duration.FromMinutes(20), London, RecurrenceRule.Daily()),
            Buffers.None, Gym);

        var problem = Assert.Single(ActivityCheck.Check(late, [], Context(gymHours: Daily(new OpenTimes(new LocalTime(18, 0), new LocalTime(2, 0))))).Problems);

        Assert.Contains("28 Mar 2027", problem.Message);
        Assert.Contains("02:30 to 02:50", problem.Message);
    }

    // H8 on every occurrence, not just the usual length: one moved to 24 hours, with 10 minutes' preparation, takes up
    // more than 24 hours. Reported, not thrown.
    [Fact]
    public void AMovedOccurrenceOver24Hours_IsAProblemWithItsLength_NotAnException()
    {
        var series = new Series(new LocalDate(2026, 11, 2), new LocalTime(18, 0), Duration.FromHours(1), London, RecurrenceRule.Daily().EndingAfter(3))
            .MoveOccurrence(new LocalDate(2026, 11, 3), new LocalDateTime(2026, 11, 3, 6, 0), Duration.FromHours(24));
        var hike = new PlannedActivity(Guid.NewGuid(), "Hike", series, new Buffers(Duration.FromMinutes(10), Duration.Zero), null);

        var problem = Assert.Single(ActivityCheck.Check(hike, [], Context()).Problems);

        Assert.Equal(CheckAspect.Length, problem.Aspect);
    }

    [Fact]
    public void ALengthOfZeroOrLess_IsAProblemWithItsLength_NotAnException()
    {
        var never = new PlannedActivity(Guid.NewGuid(), "Nothing",
            new Series(new LocalDate(2026, 11, 2), new LocalTime(18, 0), Duration.FromMinutes(-30), London, RecurrenceRule.Daily()),
            Buffers.None, null);

        Assert.Equal(CheckAspect.Length, Assert.Single(ActivityCheck.Check(never, [], Context()).Problems).Aspect);
    }

    // Two footprints of up to 24 hours can reach across three dates when one of them is the 23-hour day the clocks go
    // forward (T5): a shift from Friday 26 March 23:30 overlaps the preparation for something at midnight on Monday
    // 29 March, 20 minutes on Saturday evening (UTC).
    [Fact]
    public void AClashThreeDatesBeforeTheFirstDate_AcrossTheShortDay_IsFound()
    {
        var ultra = OneOff("Ultra", new LocalDate(2027, 3, 29), new LocalTime(0, 0), minutes: 10, at: null,
            buffers: new Buffers(Duration.FromMinutes((23 * 60) + 50), Duration.Zero));
        var longShift = OneOff("Long shift", new LocalDate(2027, 3, 26), new LocalTime(23, 30), minutes: 24 * 60, at: null);

        var clash = Assert.Single(ActivityCheck.Check(ultra, [longShift], Context()).Clashes);

        Assert.Equal(new Interval(Instant.FromUtc(2027, 3, 27, 23, 10), Instant.FromUtc(2027, 3, 27, 23, 30)), clash.Overlap);
    }

    // A closing time after midnight reads as the next day.
    [Fact]
    public void OpeningHoursPastMidnight_AreDescribedAsTheNextDay()
    {
        var hours = Daily(new OpenTimes(new LocalTime(18, 0), new LocalTime(2, 0)));

        var problem = Assert.Single(ActivityCheck.Check(GymSession(new LocalDate(2026, 11, 2), new LocalTime(12, 0)), [], Context(gymHours: hours)).Problems);

        Assert.Contains("open 18:00 to 02:00 the next day", problem.Message);
    }

    // H8: with its buffers and travel, an activity can't take up more than 24 hours.
    [Fact]
    public void AFootprintOver24Hours_IsAProblemWithItsLength()
    {
        var allDay = OneOff("Hike", new LocalDate(2026, 11, 2), new LocalTime(0, 0), minutes: 24 * 60, at: Home, buffers: new Buffers(Duration.FromMinutes(10), Duration.Zero));

        var problem = Assert.Single(ActivityCheck.Check(allDay, [], Context()).Problems);

        Assert.Equal(CheckAspect.Length, problem.Aspect);
    }

    // Q7: a whole day at home from midnight is 25 hours on Sunday 25 October 2026, when the clocks go back, and is
    // allowed then; the same 25 hours on Monday the 26th is too long.
    [Theory]
    [InlineData(25, false)]
    [InlineData(26, true)]
    public void TheWhole25HourDay_IsAllowed_OnlyOnTheDayTheClocksGoBack(int octoberDay, bool tooLong)
    {
        var wholeDay = OneOff("Festival", new LocalDate(2026, 10, octoberDay), new LocalTime(0, 0), minutes: 25 * 60, at: Home);

        var problems = ActivityCheck.Check(wholeDay, [], Context()).Problems;

        Assert.Equal(tooLong, problems.Any(problem => problem.Aspect == CheckAspect.Length));
    }

    // H8 beyond the year checked (scheduling review): a 25-hour Sunday every 60 weeks is fine on 25 October 2026, but
    // next falls on Sunday 19 December 2027, an ordinary 24-hour day.
    [Fact]
    public void A25HourRepeat_LandingOnOrdinaryDaysLater_IsAProblemWithItsLength()
    {
        var festival = new PlannedActivity(Guid.NewGuid(), "Festival",
            new Series(new LocalDate(2026, 10, 25), new LocalTime(0, 0), Duration.FromHours(25), London,
                RecurrenceRule.Weekly([IsoDayOfWeek.Sunday], everyWeeks: 60)),
            Buffers.None, null);

        Assert.Equal(CheckAspect.Length, Assert.Single(ActivityCheck.Check(festival, [], Context()).Problems).Aspect);
    }

    // Q7: the only date is Sunday 25 October 2026, so 25 hours is allowed, though the first date is the Saturday.
    [Fact]
    public void ARepeatWhoseOnlyDateIsThe25HourDay_IsAllowed()
    {
        var festival = new PlannedActivity(Guid.NewGuid(), "Festival",
            new Series(new LocalDate(2026, 10, 24), new LocalTime(0, 0), Duration.FromHours(25), London,
                RecurrenceRule.Weekly([IsoDayOfWeek.Sunday]).EndingAfter(1)),
            Buffers.None, null);

        Assert.Empty(ActivityCheck.Check(festival, [], Context()).Problems);
    }

    // P1: the length problem says what makes up the total, so a default filled in for a blank box (ACC-6) or the
    // travel isn't a hidden reason. With nothing around it, it doesn't mention preparation at all.
    [Fact]
    public void TheLengthProblem_SaysWhatMakesUpTheTotal()
    {
        var withTravel = OneOff("Hike", new LocalDate(2026, 11, 2), new LocalTime(0, 0), minutes: 23 * 60, at: Gym, buffers: new Buffers(Duration.Zero, Duration.FromMinutes(10)));
        var bare = OneOff("Festival", new LocalDate(2026, 10, 26), new LocalTime(0, 0), minutes: 25 * 60, at: Home);

        var withTravelProblem = Assert.Single(ActivityCheck.Check(withTravel, [], Context()).Problems).Message;
        var bareProblem = Assert.Single(ActivityCheck.Check(bare, [], Context()).Problems).Message;

        Assert.StartsWith("With 25 min there, 10 min changing and 45 min back, it would take up 24 h 20 min", withTravelProblem);
        Assert.StartsWith("It would take up 25 h", bareProblem);
    }

    // The long day isn't 25 hours everywhere (Lord Howe Island's clocks go back 30 minutes), so the message doesn't
    // promise 25.
    [Fact]
    public void TheLengthProblem_DoesntPromise25HoursWhereTheLongDayIsShorter()
    {
        var lordHowe = DateTimeZoneProviders.Tzdb["Australia/Lord_Howe"];
        var festival = new PlannedActivity(Guid.NewGuid(), "Festival",
            new Series(new LocalDate(2027, 4, 4), new LocalTime(0, 0), Duration.FromHours(25), lordHowe, RecurrenceRule.Daily().EndingAfter(1)),
            Buffers.None, null);

        var problem = Assert.Single(ActivityCheck.Check(festival, [], Context()).Problems).Message;

        Assert.DoesNotContain("25 on the day", problem);
    }

    // §8: "the next day" only for the next day. A 24-hour shift from 23:30 GMT on Saturday 27 March 2027 ends at 00:30
    // BST on Monday the 29th (the Sunday has 23 hours), which is said by its date.
    [Fact]
    public void AnActivityEndingTwoDatesLater_IsNotSaidToEndTheNextDay()
    {
        var longShift = OneOff("Long shift", new LocalDate(2027, 3, 27), new LocalTime(23, 30), minutes: 24 * 60, at: null);
        var call = OneOff("Call", new LocalDate(2027, 3, 29), new LocalTime(0, 0), minutes: 20, at: null);

        var clash = Assert.Single(ActivityCheck.Check(call, [longShift], Context()).Clashes);

        Assert.Equal(new Interval(Instant.FromUtc(2027, 3, 28, 23, 0), Instant.FromUtc(2027, 3, 28, 23, 20)), clash.Overlap);
        var described = ActivityCheck.Describe(clash, London);
        Assert.DoesNotContain("the next day", described);
        Assert.Contains("to 00:30 on Mon 29 Mar 2027", described);
    }

    // §7.1, P1: travel is never guessed. Without a home, or a travel time in either direction, the save is stopped and
    // the problem says what to add.
    [Fact]
    public void AnActivityAwayFromHome_WithNoHomeSet_IsAProblemWithItsLocation()
    {
        var problem = Assert.Single(ActivityCheck.Check(GymSession(new LocalDate(2026, 11, 2)), [], Context(withHome: false)).Problems);

        Assert.Equal(CheckAspect.Location, problem.Aspect);
        Assert.Contains("home", problem.Message);
    }

    [Fact]
    public void AnActivityAwayFromHome_WithATravelTimeMissing_NamesTheJourney()
    {
        var oneWayOnly = new TravelTimes(new Dictionary<Leg, Duration> { [new Leg(Home, Gym)] = Duration.FromMinutes(25) });

        var problem = Assert.Single(ActivityCheck.Check(GymSession(new LocalDate(2026, 11, 2)), [], Context(travel: oneWayOnly)).Problems);

        Assert.Equal(CheckAspect.Location, problem.Aspect);
        Assert.Contains("from Town gym to Home", problem.Message);
    }

    // An activity at no particular place, or at home, needs no travel, so no home or travel times are needed.
    [Fact]
    public void AnActivityAtNoParticularPlace_NeedsNoHome()
    {
        var reading = OneOff("Reading", new LocalDate(2026, 11, 2), new LocalTime(20, 0), minutes: 30, at: null);

        Assert.Empty(ActivityCheck.Check(reading, [], Context(withHome: false)).Problems);
    }

    // A repeat that ends before it first happens (scheduling review): Mondays only from Tuesday 6 October, ending
    // Sunday the 11th.
    [Fact]
    public void ARepeatThatNeverHappens_IsAProblemWithTheRepeat()
    {
        var never = new PlannedActivity(Guid.NewGuid(), "Gym",
            new Series(new LocalDate(2026, 10, 6), new LocalTime(18, 0), Duration.FromHours(1), London,
                RecurrenceRule.Weekly([IsoDayOfWeek.Monday]).EndingOn(new LocalDate(2026, 10, 11))),
            Buffers.None, null);

        var problem = Assert.Single(ActivityCheck.Check(never, [], Context()).Problems);

        Assert.Equal(CheckAspect.Repeat, problem.Aspect);
    }

    // Stored activities can be wrong in ways the check can't fix here: one over 24 hours (saved before this check
    // existed), or at a place whose travel time has since been deleted. They mustn't stop every other save: the first
    // is left out, the second is checked without its travel.
    [Fact]
    public void OtherActivitiesThatCantBeWorkedOutFully_DontStopTheCheck()
    {
        var tooLong = OneOff("Too long", new LocalDate(2026, 11, 2), new LocalTime(0, 0), minutes: 24 * 60, at: Home, buffers: new Buffers(Duration.FromMinutes(10), Duration.Zero));
        var pool = Guid.Parse("0199e3a0-0000-7000-8000-000000000003");
        var swim = OneOff("Swim", new LocalDate(2026, 11, 2), new LocalTime(18, 30), minutes: 30, at: pool);

        var result = ActivityCheck.Check(GymSession(new LocalDate(2026, 11, 2)), [tooLong, swim], Context());

        Assert.Equal("Swim", Assert.Single(result.Clashes).Other.Label);
    }

    // P1: when another activity can only be checked partly, the user is told, never left to assume it was all checked.
    // The gym session's walk home (no travel time back) runs into dinner, but without it the clash can't be seen.
    [Fact]
    public void AnotherActivityWhoseTravelIsUnknown_IsNamedInANote()
    {
        var oneWayOnly = new TravelTimes(new Dictionary<Leg, Duration> { [new Leg(Home, Gym)] = Duration.FromMinutes(25) });
        var dinner = OneOff("Dinner", new LocalDate(2026, 11, 2), new LocalTime(19, 30), minutes: 60, at: Home);

        var result = ActivityCheck.Check(dinner, [GymSession(new LocalDate(2026, 11, 2))], Context(travel: oneWayOnly));

        var note = Assert.Single(result.Notes);
        Assert.Contains("Gym", note);
        Assert.Contains("from Town gym to Home", note);
    }

    [Fact]
    public void AnotherActivityOver24Hours_IsNamedInANote()
    {
        var tooLong = OneOff("Too long", new LocalDate(2026, 11, 2), new LocalTime(0, 0), minutes: 24 * 60, at: Home, buffers: new Buffers(Duration.FromMinutes(10), Duration.Zero));

        var result = ActivityCheck.Check(GymSession(new LocalDate(2026, 11, 2)), [tooLong], Context());

        Assert.Contains("Too long", Assert.Single(result.Notes));
    }

    // T9, T10: another activity's own travel is part of its footprint: dinner from 19:30 runs into the gym session's
    // walk home, which ends at 19:45.
    [Fact]
    public void AnotherActivitysJourneyBack_CanClashWithTheCandidate()
    {
        var dinner = OneOff("Dinner", new LocalDate(2026, 11, 2), new LocalTime(19, 30), minutes: 60, at: Home);

        var result = ActivityCheck.Check(dinner, [GymSession(new LocalDate(2026, 11, 2))], Context());

        var clash = Assert.Single(result.Clashes);
        Assert.Equal((FootprintPart.Activity, FootprintPart.TravelBack), (clash.CandidatePart, clash.OtherPart));
        Assert.Equal(new Interval(Instant.FromUtc(2026, 11, 2, 19, 30), Instant.FromUtc(2026, 11, 2, 19, 45)), clash.Overlap);
        Assert.Empty(result.Notes);
    }

    // Q7 for other activities: the whole 25-hour day is checked for clashes like any other, not left out as too long.
    [Fact]
    public void AnotherActivityTakingTheWhole25HourDay_IsCheckedForClashes()
    {
        var festival = OneOff("Festival", new LocalDate(2026, 10, 25), new LocalTime(0, 0), minutes: 25 * 60, at: null);
        var reading = OneOff("Reading", new LocalDate(2026, 10, 25), new LocalTime(20, 0), minutes: 30, at: null);

        var result = ActivityCheck.Check(reading, [festival], Context());

        Assert.Empty(result.Notes);
        Assert.Equal(new Interval(Instant.FromUtc(2026, 10, 25, 20, 0), Instant.FromUtc(2026, 10, 25, 20, 30)), Assert.Single(result.Clashes).Overlap);
    }

    // A night shift from Monday runs into an early gym session on Tuesday: the message gives the shift's own date, or
    // "22:00 to 06:00 the next day" would read as Tuesday night.
    [Fact]
    public void AClashWithAnOvernightActivityFromTheDayBefore_NamesItsDay()
    {
        var nightShift = OneOff("Night shift", new LocalDate(2026, 11, 2), new LocalTime(22, 0), minutes: 8 * 60, at: null);
        var earlyGym = OneOff("Early gym", new LocalDate(2026, 11, 3), new LocalTime(5, 30), minutes: 50, at: null);

        var clash = Assert.Single(ActivityCheck.Check(earlyGym, [nightShift], Context()).Clashes);

        Assert.Equal(new Interval(Instant.FromUtc(2026, 11, 3, 5, 30), Instant.FromUtc(2026, 11, 3, 6, 0)), clash.Overlap);
        Assert.Equal(
            "On Tue 3 Nov 2026, it overlaps Night shift (Night shift is on Mon 2 Nov 2026 22:00 to 06:00 the next day).",
            ActivityCheck.Describe(clash, London));
    }

    // T4: 01:15 happens twice on Sunday 25 October 2026, so a time the clocks repeat says which one: a 60-minute swim
    // from the first 01:15 ends at the second.
    [Fact]
    public void AClashOnTheNightTheClocksGoBack_ShowsTimesThatCanBeToldApart()
    {
        var swim = OneOff("Night swim", new LocalDate(2026, 10, 25), new LocalTime(1, 15), minutes: 60, at: null);
        var reading = OneOff("Reading", new LocalDate(2026, 10, 25), new LocalTime(1, 30), minutes: 30, at: null);

        var clash = Assert.Single(ActivityCheck.Check(reading, [swim], Context()).Clashes);

        Assert.Equal(new Interval(Instant.FromUtc(2026, 10, 25, 0, 30), Instant.FromUtc(2026, 10, 25, 1, 0)), clash.Overlap);
        Assert.Contains("Night swim is 01:15 BST to 01:15 GMT", ActivityCheck.Describe(clash, London));
    }

    // An activity after midnight falls in the previous evening's opening; the problem gives both dates.
    [Fact]
    public void AnActivityAfterMidnight_OutsideTheHours_SaysItsOwnDate()
    {
        var hours = Daily(new OpenTimes(new LocalTime(18, 0), new LocalTime(2, 0)));

        var problem = Assert.Single(ActivityCheck.Check(GymSession(new LocalDate(2026, 11, 3), new LocalTime(1, 30)), [], Context(gymHours: hours)).Problems);

        Assert.Contains("on Mon 2 Nov 2026, but it would be there on Tue 3 Nov 2026 01:30 to 02:20", problem.Message);
    }

    // H1 within one activity: a daily 07:00 shift of 23 h 30 min runs to 06:30 UTC on Sunday 28 March 2027, but the
    // next one starts at 07:00 BST, which is 06:00 UTC, after the clocks go forward. The home page shows that clash, so
    // the check on save finds it too.
    [Fact]
    public void ARepeatingActivityThatOverlapsItsOwnNextOccurrence_Clashes()
    {
        var shift = new PlannedActivity(Guid.NewGuid(), "Long shift",
            new Series(new LocalDate(2027, 3, 27), new LocalTime(7, 0), Duration.FromMinutes((23 * 60) + 30), London, RecurrenceRule.Daily()),
            Buffers.None, null);

        var result = ActivityCheck.Check(shift, [], Context() with { Today = new LocalDate(2027, 3, 27) });

        var clash = Assert.Single(result.Clashes);
        Assert.Equal(new Interval(Instant.FromUtc(2027, 3, 28, 6, 0), Instant.FromUtc(2027, 3, 28, 6, 30)), clash.Overlap);
    }

    // Gym 18:00-18:50 at Town gym: 25 minutes there, 10 minutes changing, 45 minutes back (T1).
    // H5, T13, SCH-1: a demanding session closer than the preferences allow to another is refused (§7.2: a validation
    // error), naming the other one and the gap. Hard ride Monday 20:00; intervals Tuesday 18:00, 22 hours later.
    [Fact]
    public void ADemandingSessionTooCloseToAnother_IsAProblem()
    {
        var ride = OneOff("Hard ride", new LocalDate(2026, 11, 2), new LocalTime(20, 0), minutes: 60, at: null) with { IsDemanding = true };
        var intervals = OneOff("Intervals", new LocalDate(2026, 11, 3), new LocalTime(18, 0), minutes: 45, at: null) with { IsDemanding = true };

        var problem = Assert.Single(ActivityCheck.Check(intervals, [ride], WithRest(hours: 24)).Problems);

        Assert.Equal(CheckAspect.Time, problem.Aspect);
        Assert.Equal(
            "It's 22 h from Hard ride, a demanding session on Mon 2 Nov 2026 at 20:00; your preferences keep demanding sessions at least 24 hours apart.",
            problem.Message);
    }

    // Exactly the time apart is fine (start to start), and a session that isn't demanding isn't held to it.
    [Theory]
    [InlineData(20, true, 0)]
    [InlineData(18, false, 0)]
    [InlineData(18, true, 1)]
    public void TheRestRule_AppliesToDemandingSessionsOnly_FromStartToStart(int tuesdayHour, bool demanding, int problems)
    {
        var ride = OneOff("Hard ride", new LocalDate(2026, 11, 2), new LocalTime(20, 0), minutes: 60, at: null) with { IsDemanding = true };
        var session = OneOff("Session", new LocalDate(2026, 11, 3), new LocalTime(tuesdayHour, 0), minutes: 45, at: null) with { IsDemanding = demanding };

        Assert.Equal(problems, ActivityCheck.Check(session, [ride], WithRest(hours: 24)).Problems.Count);
    }

    // H6: no more demanding sessions starting in a day than the preferences allow (here 1).
    [Fact]
    public void ADemandingSessionOnADayWithItsMostAlready_IsAProblem()
    {
        var ride = OneOff("Hard ride", new LocalDate(2026, 11, 2), new LocalTime(7, 0), minutes: 60, at: null) with { IsDemanding = true };
        var intervals = OneOff("Intervals", new LocalDate(2026, 11, 2), new LocalTime(18, 0), minutes: 45, at: null) with { IsDemanding = true };

        var problem = Assert.Single(ActivityCheck.Check(intervals, [ride], WithRest(hours: 0)).Problems);

        Assert.Equal("On Mon 2 Nov 2026 there's already a demanding session, Hard ride; your preferences allow 1 a day.", problem.Message);
    }

    // H7: no demanding session on a recovery day; every occurrence checked, the first named and the rest counted.
    // Wednesdays from 4 November, checked for 53 weeks (as H4 and H8 are) to Tuesday 9 November 2027: 53 Wednesdays.
    [Fact]
    public void ADemandingSessionOnARecoveryDay_IsAProblem()
    {
        var wednesdays = Weekly(new LocalDate(2026, 11, 4), [IsoDayOfWeek.Wednesday], new LocalTime(18, 0)) with { IsDemanding = true };

        var problem = Assert.Single(ActivityCheck.Check(wednesdays, [], WithRest(hours: 0, recovery: [IsoDayOfWeek.Wednesday])).Problems);

        Assert.Equal(
            "Wed 4 Nov 2026 is one of your recovery days, and this is a demanding session. The same happens on 52 more dates up to Tue 9 Nov 2027.",
            problem.Message);
    }

    // H7: a rule every 10 days reaches Friday first on 1 January 2027, past the 8-week clash window (scheduling review,
    // Phase 6: the rest rules were only checked that far, unlike H4 and H8, which depend on the weekday too).
    [Fact]
    public void ARecoveryDay_IsFound_ForARuleEveryTenDays()
    {
        var everyTenDays = new PlannedActivity(Guid.NewGuid(), "Intervals",
            new Series(new LocalDate(2026, 11, 2), new LocalTime(18, 0), Duration.FromMinutes(45), London, RecurrenceRule.Daily(10)), Buffers.None, null)
        {
            IsDemanding = true,
        };

        var problem = Assert.Single(ActivityCheck.Check(everyTenDays, [], WithRest(hours: 0, recovery: [IsoDayOfWeek.Friday])).Problems);

        Assert.StartsWith("Fri 1 Jan 2027 is one of your recovery days, and this is a demanding session.", problem.Message);
    }

    // H5, T5, §8: Saturday 18:00 GMT to Sunday 18:00 BST on 27-28 March 2027 is 23 hours, though every other weekend
    // is 24 or 25 (the planner refuses that slot too: TheRestGap_IsElapsedTime_OnTheNightTheClocksGoForward).
    [Fact]
    public void TheRestRule_IsKeptOnTheNightTheClocksGoForward()
    {
        var saturdays = Weekly(new LocalDate(2026, 10, 24), [IsoDayOfWeek.Saturday], new LocalTime(18, 0)) with { Title = "Long run", IsDemanding = true };
        var sundays = Weekly(new LocalDate(2026, 10, 25), [IsoDayOfWeek.Sunday], new LocalTime(18, 0)) with { Title = "Intervals", IsDemanding = true };

        var problem = Assert.Single(ActivityCheck.Check(sundays, [saturdays], WithRest(hours: 24)).Problems);

        Assert.StartsWith("It's 23 h from Long run, a demanding session on Sat 27 Mar 2027 at 18:00", problem.Message);
    }

    // The other activities to load for a demanding one reach as far as its rest rules are checked: 53 weeks.
    [Fact]
    public void OthersThatCanClash_ForADemandingActivity_ReachAsFarAsTheRestRulesAreChecked()
    {
        var series = Weekly(new LocalDate(2026, 10, 25), [IsoDayOfWeek.Sunday], new LocalTime(18, 0)).Series;

        Assert.True(ActivityCheck.OthersThatCanClash(series, Today, Duration.FromHours(24), demanding: true).Contains(new LocalDate(2027, 3, 27)));
        Assert.False(ActivityCheck.OthersThatCanClash(series, Today, Duration.FromHours(24)).Contains(new LocalDate(2027, 3, 27)));
    }

    // H5 within the activity itself: demanding every day, with 48 hours between demanding sessions.
    [Fact]
    public void ADailyDemandingSession_WithTwoDaysRest_BreaksTheRuleWithItself()
    {
        var daily = new PlannedActivity(Guid.NewGuid(), "Intervals",
            new Series(new LocalDate(2026, 11, 2), new LocalTime(18, 0), Duration.FromMinutes(45), London, RecurrenceRule.Daily()), Buffers.None, null)
        {
            IsDemanding = true,
        };

        var problem = Assert.Single(ActivityCheck.Check(daily, [], WithRest(hours: 48)).Problems);

        Assert.StartsWith("It's 24 h from Intervals, a demanding session on Tue 3 Nov 2026 at 18:00", problem.Message);
    }

    // CAL-4, SCH-1: moving one occurrence of a demanding activity next to another demanding session is refused too.
    [Fact]
    public void CheckOccurrence_KeepsTheRestRule()
    {
        var ride = OneOff("Hard ride", new LocalDate(2026, 11, 5), new LocalTime(7, 0), minutes: 60, at: null) with { IsDemanding = true };
        var gym = AtHome(GymMondaysAndWednesdays().MoveOccurrence(new LocalDate(2026, 11, 4), new LocalDateTime(2026, 11, 4, 18, 0), Duration.FromHours(1))) with { IsDemanding = true };

        var problem = Assert.Single(ActivityCheck.CheckOccurrence(gym, new LocalDate(2026, 11, 4), [ride], WithRest(hours: 24)).Problems);

        Assert.StartsWith("It's 13 h from Hard ride", problem.Message);
    }

    // H5, H6, §7.2: another activity's demanding session at the very same time and length isn't the candidate's own
    // occurrence (scheduling review, Phase 6: it was taken for it, leaving only a clash that could be saved anyway).
    [Fact]
    public void ADemandingSessionAtTheSameTimeAsAnother_BreaksTheRestRules()
    {
        var ride = OneOff("Hard ride", new LocalDate(2026, 11, 2), new LocalTime(18, 0), minutes: 60, at: null) with { IsDemanding = true };
        var intervals = OneOff("Intervals", new LocalDate(2026, 11, 2), new LocalTime(18, 0), minutes: 60, at: null) with { IsDemanding = true };

        var result = ActivityCheck.Check(intervals, [ride], WithRest(hours: 24));

        Assert.Equal(
            [
                "It's 0 min from Hard ride, a demanding session on Mon 2 Nov 2026 at 18:00; your preferences keep demanding sessions at least 24 hours apart.",
                "On Mon 2 Nov 2026 there's already a demanding session, Hard ride; your preferences allow 1 a day.",
            ],
            result.Problems.Select(problem => problem.Message));
        Assert.Empty(result.Clashes);
    }

    // The other activities to load reach as far as the rest rule: 96 hours needs those from 5 days before.
    [Fact]
    public void OthersThatCanClash_ReachAsFarAsTheRestRule()
    {
        var series = GymSession(new LocalDate(2026, 11, 2)).Series;

        var dates = ActivityCheck.OthersThatCanClash(series, Today, Duration.FromHours(96));

        Assert.Equal(new LocalDate(2026, 10, 28), dates.Start);
    }

    private static PlanningContext WithRest(int hours, IsoDayOfWeek[]? recovery = null) =>
        Context() with { Rest = new RestRules(Duration.FromHours(hours), 1, new HashSet<IsoDayOfWeek>(recovery ?? [])) };

    private static PlannedActivity GymSession(LocalDate date, LocalTime? start = null) =>
        OneOff("Gym", date, start ?? new LocalTime(18, 0), minutes: 50, at: Gym, buffers: new Buffers(Duration.Zero, Duration.FromMinutes(10)));

    private static PlannedActivity OneOff(string title, LocalDate date, LocalTime start, int minutes, Guid? at, Buffers? buffers = null) =>
        new(Guid.NewGuid(), title,
            new Series(date, start, Duration.FromMinutes(minutes), London, RecurrenceRule.Daily().EndingAfter(1)),
            buffers ?? Buffers.None, at);

    private static PlannedActivity Weekly(LocalDate firstDate, IsoDayOfWeek[] days, LocalTime start, Guid? at = null) =>
        new(Guid.NewGuid(), "Gym",
            new Series(firstDate, start, Duration.FromMinutes(50), London, RecurrenceRule.Weekly(days)),
            Buffers.None, at ?? Home);

    private static OpeningHours Daily(OpenTimes hours) =>
        new(Enum.GetValues<IsoDayOfWeek>().Where(day => day != IsoDayOfWeek.None).ToDictionary(day => day, _ => hours));

    private static PlanningContext Context(bool withHome = true, OpeningHours? gymHours = null, TravelTimes? travel = null) =>
        new(Today,
            withHome ? Home : null,
            travel ?? new TravelTimes(new Dictionary<Leg, Duration>
            {
                [new Leg(Home, Gym)] = Duration.FromMinutes(25),
                [new Leg(Gym, Home)] = Duration.FromMinutes(45),
            }),
            new Dictionary<Guid, Place>
            {
                [Home] = new("Home", OpeningHours.Always),
                [Gym] = new("Town gym", gymHours ?? OpeningHours.Always),
            });
}
