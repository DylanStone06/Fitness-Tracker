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

        Assert.Contains("Town gym is closed on Sun 8 Nov 2026", problem.Message);
        Assert.Contains("7 more", problem.Message);
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
