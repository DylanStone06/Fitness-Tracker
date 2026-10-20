using FitnessTracker.Domain.Footprints;
using FitnessTracker.Domain.Locations;
using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Domain.Scheduling;
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

    // Gym 18:00-18:50 at Town gym: 25 minutes there, 10 minutes changing, 45 minutes back (T1).
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
