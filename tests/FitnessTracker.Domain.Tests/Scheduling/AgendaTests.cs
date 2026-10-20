using FitnessTracker.Domain.Footprints;
using FitnessTracker.Domain.Locations;
using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Domain.Scheduling;
using NodaTime;

namespace FitnessTracker.Domain.Tests.Scheduling;

// GOL-2, first part: the home page's "today and the next 7 days". Each occurrence starting on the dates asked for, in
// time order, with its whole footprint (when to leave, when back), and what it clashes with (CAL-6).
public class AgendaTests
{
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly Guid Home = Guid.Parse("0199e3a0-0000-7000-8000-000000000001");
    private static readonly Guid Gym = Guid.Parse("0199e3a0-0000-7000-8000-000000000002");
    private static readonly LocalDate Monday = new(2026, 11, 2);

    [Fact]
    public void Occurrences_AreListedInTimeOrder_OnlyForTheDatesAsked()
    {
        var gym = Weekly("Gym", [IsoDayOfWeek.Monday, IsoDayOfWeek.Wednesday], new LocalTime(18, 0), at: null);
        var dentist = OneOff("Dentist", Monday.PlusDays(1), new LocalTime(9, 0), minutes: 30, at: null);

        var agenda = Agenda.Between([gym, dentist], Context(), from: Monday, through: Monday.PlusDays(2));

        Assert.Equal(
            [("Gym", Monday), ("Dentist", Monday.PlusDays(1)), ("Gym", Monday.PlusDays(2))],
            agenda.Select(item => (item.Activity.Title, item.Occurrence.Date)));
    }

    // T1: gym 18:00-18:50 at Town gym with 25 minutes there, 10 minutes changing and 45 minutes back: leave at 17:35,
    // home at 19:45 (GMT, so the same in UTC).
    [Fact]
    public void EachOccurrence_HasItsWholeFootprint()
    {
        var gym = OneOff("Gym", Monday, new LocalTime(18, 0), minutes: 50, at: Gym, buffers: new Buffers(Duration.Zero, Duration.FromMinutes(10)));

        var item = Assert.Single(Agenda.Between([gym], Context(), Monday, Monday));

        Assert.Equal(new Interval(Instant.FromUtc(2026, 11, 2, 17, 35), Instant.FromUtc(2026, 11, 2, 19, 45)), item.Footprint?.Whole);
        Assert.Null(item.Note);
        Assert.Equal("Leave home 17:35, back 19:45", Agenda.Trip(item));
    }

    // Getting ready happens at home before setting off (§7.1): with 15 minutes' preparation, still leave at 17:35.
    [Fact]
    public void LeaveHome_IsAfterGettingReady()
    {
        var gym = OneOff("Gym", Monday, new LocalTime(18, 0), minutes: 50, at: Gym, buffers: new Buffers(Duration.FromMinutes(15), Duration.FromMinutes(10)));

        Assert.Equal("Leave home 17:35, back 19:45", Agenda.Trip(Assert.Single(Agenda.Between([gym], Context(), Monday, Monday))));
    }

    // §8, crossing midnight: a late session gets back the next day, and one just after midnight leaves the day before.
    [Theory]
    [InlineData(22, 30, "Leave home 22:05, back 00:15 the next day")]
    [InlineData(0, 10, "Leave home 23:45 the day before, back 01:55")]
    public void TimesOnAnotherDay_SaySo(int hour, int minute, string expected)
    {
        var gym = OneOff("Gym", Monday, new LocalTime(hour, minute), minutes: 50, at: Gym, buffers: new Buffers(Duration.Zero, Duration.FromMinutes(10)));

        Assert.Equal(expected, Agenda.Trip(Assert.Single(Agenda.Between([gym], Context(), Monday, Monday))));
    }

    // T4: 01:15 happens twice on Sunday 25 October 2026. Gym from 00:30 BST gets back at the second 01:15, so it says
    // which: the trip is 2 h 10 min, not 1 h 10 min.
    [Fact]
    public void ATimeTheClocksRepeat_SaysWhichOne()
    {
        var sunday = new LocalDate(2026, 10, 25);
        var gym = OneOff("Gym", sunday, new LocalTime(0, 30), minutes: 50, at: Gym, buffers: new Buffers(Duration.Zero, Duration.FromMinutes(10)));

        var item = Assert.Single(Agenda.Between([gym], Context(), sunday, sunday));

        Assert.Equal(new Interval(Instant.FromUtc(2026, 10, 24, 23, 5), Instant.FromUtc(2026, 10, 25, 1, 15)), item.Footprint?.Whole);
        Assert.Equal("Leave home 00:05, back 01:15 GMT", Agenda.Trip(item));
    }

    // Buffers at home: no leaving, but the time it takes up is shown.
    [Fact]
    public void AnActivityAtHomeWithBuffers_ShowsTheTimeItTakesUp()
    {
        var yoga = OneOff("Yoga", Monday, new LocalTime(7, 0), minutes: 30, at: Home, buffers: new Buffers(Duration.FromMinutes(5), Duration.FromMinutes(10)));

        Assert.Equal("With getting ready and changing: 06:55 to 07:40", Agenda.Trip(Assert.Single(Agenda.Between([yoga], Context(), Monday, Monday))));
    }

    // P1: travel that can't be worked out isn't guessed: the item says why, and its footprint is without travel.
    [Fact]
    public void AnOccurrenceWhoseTravelIsUnknown_SaysWhy()
    {
        var oneWayOnly = new TravelTimes(new Dictionary<Leg, Duration> { [new Leg(Home, Gym)] = Duration.FromMinutes(25) });
        var gym = OneOff("Gym", Monday, new LocalTime(18, 0), minutes: 50, at: Gym);

        var item = Assert.Single(Agenda.Between([gym], Context(travel: oneWayOnly), Monday, Monday));

        Assert.Contains("no travel time from Town gym to Home", item.Note);
        Assert.Equal(new Interval(Instant.FromUtc(2026, 11, 2, 18, 0), Instant.FromUtc(2026, 11, 2, 18, 50)), item.Footprint?.Whole);
    }

    [Fact]
    public void AnOccurrenceAwayFromHome_WithNoHomeMarked_SaysSo()
    {
        var gym = OneOff("Gym", Monday, new LocalTime(18, 0), minutes: 50, at: Gym);

        var item = Assert.Single(Agenda.Between([gym], Context() with { Home = null }, Monday, Monday));

        Assert.Contains("none of your locations is marked as home", item.Note);
    }

    // H8: a footprint over 24 hours (a travel time changed since it was saved) is said, not silently left out.
    [Fact]
    public void AnOccurrenceOver24Hours_SaysSo()
    {
        var long16Hours = OneOff("Festival", Monday, new LocalTime(6, 0), minutes: 16 * 60, at: Gym);
        var slowWayBack = new TravelTimes(new Dictionary<Leg, Duration>
        {
            [new Leg(Home, Gym)] = Duration.FromMinutes(25),
            [new Leg(Gym, Home)] = Duration.FromMinutes(600),
        });

        var item = Assert.Single(Agenda.Between([long16Hours], Context(travel: slowWayBack), Monday, Monday));

        Assert.Null(item.Footprint);
        Assert.Contains("more than 24 hours", item.Note);
    }

    // CAL-6: a clash shows on both items. The gym session's walk home runs into dinner at 19:30.
    [Fact]
    public void AClash_IsShownOnBothItems()
    {
        var gym = OneOff("Gym", Monday, new LocalTime(18, 0), minutes: 50, at: Gym);
        var dinner = OneOff("Dinner", Monday, new LocalTime(19, 30), minutes: 60, at: null);
        var reading = OneOff("Reading", Monday, new LocalTime(21, 0), minutes: 30, at: null);

        var agenda = Agenda.Between([gym, dinner, reading], Context(), Monday, Monday);

        Assert.Equal(["Dinner"], agenda.Single(item => item.Activity.Title == "Gym").ClashesWith);
        Assert.Equal(["Gym"], agenda.Single(item => item.Activity.Title == "Dinner").ClashesWith);
        Assert.Empty(agenda.Single(item => item.Activity.Title == "Reading").ClashesWith);
    }

    // An activity from the day before isn't listed, but a clash with it still shows on the day asked for: a night
    // shift from Sunday 22:00 runs into a Monday 05:30 gym session.
    [Fact]
    public void AClashWithSomethingFromTheDayBefore_IsStillShown()
    {
        var nightShift = OneOff("Night shift", Monday.PlusDays(-1), new LocalTime(22, 0), minutes: 8 * 60, at: null);
        var earlyGym = OneOff("Early gym", Monday, new LocalTime(5, 30), minutes: 50, at: null);

        var item = Assert.Single(Agenda.Between([nightShift, earlyGym], Context(), Monday, Monday));

        Assert.Equal("Early gym", item.Activity.Title);
        Assert.Equal(["Night shift"], item.ClashesWith);
    }

    // Same input, same output, whatever order the activities come in (SCH-7): ties go by title.
    [Fact]
    public void ActivitiesAtTheSameTime_AreListedInTitleOrder_WhateverOrderTheyComeIn()
    {
        var b = OneOff("B", Monday, new LocalTime(9, 0), minutes: 30, at: null);
        var a = OneOff("A", Monday, new LocalTime(9, 0), minutes: 30, at: null);

        Assert.Equal(["A", "B"], Agenda.Between([b, a], Context(), Monday, Monday).Select(item => item.Activity.Title));
        Assert.Equal(["A", "B"], Agenda.Between([a, b], Context(), Monday, Monday).Select(item => item.Activity.Title));
    }

    // SCH-7 with a full tie: two "Gym" sessions at 18:00 (one at home, one at the gym) come out in the same order
    // whichever order the database returns them in.
    [Fact]
    public void SameTitleAndStart_SameOrder_WhateverOrderTheyComeIn()
    {
        var atHome = OneOff("Gym", Monday, new LocalTime(18, 0), minutes: 50, at: null);
        var atGym = OneOff("Gym", Monday, new LocalTime(18, 0), minutes: 50, at: Gym);

        Assert.Equal(
            Agenda.Between([atHome, atGym], Context(), Monday, Monday).Select(item => item.Activity.Id),
            Agenda.Between([atGym, atHome], Context(), Monday, Monday).Select(item => item.Activity.Id));
    }

    private static PlannedActivity OneOff(string title, LocalDate date, LocalTime start, int minutes, Guid? at, Buffers? buffers = null) =>
        new(Guid.NewGuid(), title,
            new Series(date, start, Duration.FromMinutes(minutes), London, RecurrenceRule.Daily().EndingAfter(1)),
            buffers ?? Buffers.None, at);

    private static PlannedActivity Weekly(string title, IsoDayOfWeek[] days, LocalTime start, Guid? at) =>
        new(Guid.NewGuid(), title,
            new Series(Monday, start, Duration.FromMinutes(50), London, RecurrenceRule.Weekly(days)),
            Buffers.None, at);

    private static PlanningContext Context(TravelTimes? travel = null) =>
        new(Monday,
            Home,
            travel ?? new TravelTimes(new Dictionary<Leg, Duration>
            {
                [new Leg(Home, Gym)] = Duration.FromMinutes(25),
                [new Leg(Gym, Home)] = Duration.FromMinutes(45),
            }),
            new Dictionary<Guid, Place>
            {
                [Home] = new("Home", OpeningHours.Always),
                [Gym] = new("Town gym", OpeningHours.Always),
            });
}
