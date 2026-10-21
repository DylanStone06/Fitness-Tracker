using FitnessTracker.Domain.Footprints;
using FitnessTracker.Domain.Locations;
using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Domain.Time;
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

    // §8: two dates later is said by its date. A 24-hour session from 23:30 on the 25-hour Sunday 25 October 2026, with
    // 30 minutes' changing, ends at midnight starting Tuesday the 27th.
    [Fact]
    public void ATimeTwoDatesLater_SaysItsDate()
    {
        var sunday = new LocalDate(2026, 10, 25);
        var marathon = OneOff("Marathon", sunday, new LocalTime(23, 30), minutes: 24 * 60, at: Home, buffers: new Buffers(Duration.Zero, Duration.FromMinutes(30)));

        Assert.Equal(
            "With getting ready and changing: 23:30 to 00:00 on Tue 27 Oct 2026",
            Agenda.Trip(Assert.Single(Agenda.Between([marathon], Context(), sunday, sunday))));
    }

    // Q7 on the agenda: the whole 25-hour day has a footprint, so its clashes are found and nothing is said against it.
    [Fact]
    public void TheWhole25HourDay_HasAFootprint()
    {
        var sunday = new LocalDate(2026, 10, 25);

        var item = Assert.Single(Agenda.Between([OneOff("Festival", sunday, new LocalTime(0, 0), minutes: 25 * 60, at: null)], Context(), sunday, sunday));

        Assert.Equal(new Interval(Instant.FromUtc(2026, 10, 24, 23, 0), Instant.FromUtc(2026, 10, 26, 0, 0)), item.Footprint?.Whole);
        Assert.Null(item.Note);
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
        Assert.Contains("more than a day allows", item.Note);
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

    // GOL-2, "the next activity": at 13:00 the morning meeting is over, so the evening gym session is next. London is
    // on GMT on 2 November, so these clock times are UTC too.
    [Fact]
    public void Next_IsTheFirstActivityNotYetOver()
    {
        var meeting = OneOff("Meeting", Monday, new LocalTime(9, 0), minutes: 30, at: null);
        var gym = OneOff("Gym", Monday, new LocalTime(18, 0), minutes: 50, at: Gym);
        var agenda = Agenda.Between([meeting, gym], Context(), Monday, Monday);

        var next = Agenda.Next(agenda, Instant.FromUtc(2026, 11, 2, 13, 0));

        Assert.Equal("Gym", next?.Item.Activity.Title);
        Assert.False(next?.UnderWay);
    }

    // Walking home from the gym (it ended at 18:50; back at 19:45) still counts: the session is under way until you're
    // back. At 19:45 it's over (half-open, §8), and nothing else is left.
    [Theory]
    [InlineData(17, 35, true)]
    [InlineData(19, 30, true)]
    [InlineData(19, 45, false)]
    public void AnActivity_IsUnderWay_FromLeavingUntilBack(int hour, int minute, bool stillNext)
    {
        var gym = OneOff("Gym", Monday, new LocalTime(18, 0), minutes: 50, at: Gym, buffers: new Buffers(Duration.Zero, Duration.FromMinutes(10)));
        var agenda = Agenda.Between([gym], Context(), Monday, Monday);

        var next = Agenda.Next(agenda, Instant.FromUtc(2026, 11, 2, hour, minute));

        Assert.Equal(stillNext, next is not null);
        Assert.True(next?.UnderWay ?? true);
    }

    // What you have to set off for first is next, even when something else starts before it arrives: leaving for the
    // 18:00 gym session at 17:35 comes before a 17:40 call at home (the two clash, and both say so).
    [Fact]
    public void Next_IsWhatYouSetOffForFirst()
    {
        var gym = OneOff("Gym", Monday, new LocalTime(18, 0), minutes: 50, at: Gym);
        var call = OneOff("Call", Monday, new LocalTime(17, 40), minutes: 10, at: null);
        var agenda = Agenda.Between([gym, call], Context(), Monday, Monday);

        Assert.Equal("Gym", Agenda.Next(agenda, Instant.FromUtc(2026, 11, 2, 17, 0))?.Item.Activity.Title);
    }

    // An occurrence with no footprint (longer than the day allows, H8) is next until its own end.
    [Fact]
    public void AnOccurrenceWithNoFootprint_IsNextUntilItEnds()
    {
        var festival = OneOff("Festival", Monday, new LocalTime(6, 0), minutes: 16 * 60, at: Gym);
        var slowWayBack = new TravelTimes(new Dictionary<Leg, Duration>
        {
            [new Leg(Home, Gym)] = Duration.FromMinutes(25),
            [new Leg(Gym, Home)] = Duration.FromMinutes(600),
        });
        var agenda = Agenda.Between([festival], Context(travel: slowWayBack), Monday, Monday);

        Assert.True(Agenda.Next(agenda, Instant.FromUtc(2026, 11, 2, 21, 59))?.UnderWay);
        Assert.Null(Agenda.Next(agenda, Instant.FromUtc(2026, 11, 2, 22, 0)));
    }

    // SCH-7: setting off for the 18:00 gym session at 17:35 ties with a 17:35 call at home (a clash kept with "Save
    // anyway"). The answer is the agenda's first, the call (earlier start), whatever order the items come in.
    [Fact]
    public void Next_WithATie_IsTheSameWhateverOrderTheItemsComeIn()
    {
        var gym = OneOff("Gym", Monday, new LocalTime(18, 0), minutes: 50, at: Gym);
        var call = OneOff("Call", Monday, new LocalTime(17, 35), minutes: 10, at: null);
        var agenda = Agenda.Between([gym, call], Context(), Monday, Monday);
        var now = Instant.FromUtc(2026, 11, 2, 17, 0);

        Assert.Equal("Call", Agenda.Next(agenda, now)?.Item.Activity.Title);
        Assert.Equal("Call", Agenda.Next([.. agenda.Reverse()], now)?.Item.Activity.Title);
    }

    // T4, §8: 01:30 on Sunday 25 October 2026 is the first 01:30 (BST), 00:30 UTC; 50 minutes end at 01:20 UTC (01:20
    // GMT). At the second 01:15 (GMT) it's under way, although the clock reads earlier than 01:30.
    [Theory]
    [InlineData(0, 29, true, false)]
    [InlineData(1, 15, true, true)]
    [InlineData(1, 20, false, false)]
    public void OnTheNightTheClocksGoBack_UnderWayFollowsTheMoment(int hour, int minute, bool isNext, bool underWay)
    {
        var sunday = new LocalDate(2026, 10, 25);
        var late = OneOff("Late session", sunday, new LocalTime(1, 30), minutes: 50, at: null);

        var next = Agenda.Next(Agenda.Between([late], Context(), sunday, sunday), Instant.FromUtc(2026, 10, 25, hour, minute));

        Assert.Equal(isNext, next is not null);
        Assert.Equal(underWay, next?.UnderWay ?? false);
    }

    // T5, §8: 01:30 on Sunday 28 March 2027 doesn't exist and happens at 02:30 BST, 01:30 UTC.
    [Theory]
    [InlineData(1, 0, false)]
    [InlineData(1, 30, true)]
    public void OnTheNightTheClocksGoForward_ItIsUnderWayFrom0230(int hour, int minute, bool underWay)
    {
        var sunday = new LocalDate(2027, 3, 28);
        var late = OneOff("Late session", sunday, new LocalTime(1, 30), minutes: 50, at: null);

        var next = Agenda.Next(Agenda.Between([late], Context(), sunday, sunday), Instant.FromUtc(2027, 3, 28, hour, minute));

        Assert.NotNull(next);
        Assert.Equal(underWay, next.UnderWay);
    }

    // CAL-1: the calendar shows what overlaps the days on screen. Monday 2 November 2026 (GMT): Sunday's 22:00 night
    // shift runs into it, Sunday's 09:00 meeting doesn't.
    [Fact]
    public void Overlapping_IncludesWhatRunsIntoTheWindow_FromTheDayBefore()
    {
        var nightShift = OneOff("Night shift", Monday.PlusDays(-1), new LocalTime(22, 0), minutes: 8 * 60, at: null);
        var meeting = OneOff("Meeting", Monday.PlusDays(-1), new LocalTime(9, 0), minutes: 30, at: null);
        var gym = OneOff("Gym", Monday, new LocalTime(18, 0), minutes: 50, at: Gym);

        var items = Agenda.Overlapping([nightShift, meeting, gym], Context(), MondayWindow(), London);

        Assert.Equal(["Night shift", "Gym"], items.Select(item => item.Activity.Title));
    }

    // Half-open (§8): something ending at 00:00 Monday, or starting at 00:00 Tuesday, isn't on Monday.
    [Fact]
    public void Overlapping_IsHalfOpen()
    {
        var endsAtMidnight = OneOff("Late film", Monday.PlusDays(-1), new LocalTime(22, 0), minutes: 120, at: null);
        var startsAtMidnight = OneOff("Night swim", Monday.PlusDays(1), new LocalTime(0, 0), minutes: 60, at: null);

        Assert.Empty(Agenda.Overlapping([endsAtMidnight, startsAtMidnight], Context(), MondayWindow(), London));
    }

    // CAL-5: the footprint counts. A 00:10 Tuesday session at the gym sets off at 23:45 Monday, so Monday shows it.
    [Fact]
    public void Overlapping_CountsTheFootprint()
    {
        var earlyGym = OneOff("Early gym", Monday.PlusDays(1), new LocalTime(0, 10), minutes: 50, at: Gym);

        var item = Assert.Single(Agenda.Overlapping([earlyGym], Context(), MondayWindow(), London));

        Assert.Equal(Instant.FromUtc(2026, 11, 2, 23, 45), item.Footprint?.Whole.Start);
    }

    // ACC-3: with the 12-hour clock chosen, when to leave and the start read that way, including which 01:30 on the
    // night the clocks go back (T4).
    [Fact]
    public void WithTheTwelveHourClock_TimesReadThatWay()
    {
        var gym = OneOff("Gym", Monday, new LocalTime(18, 0), minutes: 50, at: Gym, buffers: new Buffers(Duration.Zero, Duration.FromMinutes(10)));
        var sunday = new LocalDate(2026, 10, 25);
        var late = OneOff("Late swim", sunday, new LocalTime(1, 30), minutes: 20, at: null);

        var item = Assert.Single(Agenda.Between([gym], Context(), Monday, Monday));
        var repeated = Assert.Single(Agenda.Between([late], Context(), sunday, sunday));

        Assert.Equal("Leave home 5:35pm, back 7:45pm", Agenda.Trip(item, ClockFormat.TwelveHour));
        Assert.Equal("6:00pm", Agenda.Starts(item, ClockFormat.TwelveHour));
        Assert.Equal("1:30am BST", Agenda.Starts(repeated, ClockFormat.TwelveHour));
    }

    // §8 (from the scheduling review's gaps): the day the clocks go back is 25 hours. Its window is [Sat 23:00 UTC, Mon
    // 00:00 UTC): 23:30 GMT on Sunday is in it, 00:00 on Monday isn't.
    [Fact]
    public void Overlapping_TheDayTheClocksGoBack_IsAll25Hours()
    {
        var sunday = new LocalDate(2026, 10, 25);
        var late = OneOff("Late", sunday, new LocalTime(23, 30), minutes: 20, at: null);
        var monday = OneOff("Monday", sunday.PlusDays(1), new LocalTime(0, 0), minutes: 20, at: null);
        var window = new Interval(Instant.FromUtc(2026, 10, 24, 23, 0), Instant.FromUtc(2026, 10, 26, 0, 0));

        Assert.Equal(["Late"], Agenda.Overlapping([late, monday], Context(), window, London).Select(item => item.Activity.Title));
    }

    // ... and the day they go forward is 23: [Sun 00:00 UTC, Sun 23:00 UTC). 23:30 BST is Sunday 22:30 UTC, in it.
    [Fact]
    public void Overlapping_TheDayTheClocksGoForward_IsAll23Hours()
    {
        var sunday = new LocalDate(2027, 3, 28);
        var late = OneOff("Late", sunday, new LocalTime(23, 30), minutes: 20, at: null);
        var monday = OneOff("Monday", sunday.PlusDays(1), new LocalTime(0, 0), minutes: 20, at: null);
        var window = new Interval(Instant.FromUtc(2027, 3, 28, 0, 0), Instant.FromUtc(2027, 3, 28, 23, 0));

        Assert.Equal(["Late"], Agenda.Overlapping([late, monday], Context(), window, London).Select(item => item.Activity.Title));
    }

    // The dates to load for Monday: two days before and one after it (what Overlapping looks at), and the 3-day margin
    // for clashes either side.
    [Fact]
    public void DatesToLoadFor_AWindow_AreItsDatesAndTheMargins()
    {
        Assert.Equal(new DateInterval(Monday.PlusDays(-5), Monday.PlusDays(5)), Agenda.DatesToLoadFor(MondayWindow(), London));
    }

    private static Interval MondayWindow() => new(Instant.FromUtc(2026, 11, 2, 0, 0), Instant.FromUtc(2026, 11, 3, 0, 0));

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
