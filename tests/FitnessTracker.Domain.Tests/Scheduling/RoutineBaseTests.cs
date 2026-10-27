using FitnessTracker.Domain.Footprints;
using FitnessTracker.Domain.Locations;
using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Domain.Templates;
using NodaTime;

namespace FitnessTracker.Domain.Tests.Scheduling;

// TRV-4, T15: a routine can have its own base ("Brighton weekend": the Brighton flat), and on the dates it is in use,
// travel starts and ends there instead of at home (§7.1). Today is Monday 19 October 2026; the Brighton weekend is
// chosen for Saturday 7 and Sunday 8 November 2026 (GMT, so UTC). Home to the town gym is 25 minutes, back 45; the
// flat to the gym is an hour each way when the travel times are known.
public class RoutineBaseTests
{
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly LocalDate Today = new(2026, 10, 19);
    private static readonly Guid Home = Guid.Parse("0199e3a0-0000-7000-8000-000000000001");
    private static readonly Guid Gym = Guid.Parse("0199e3a0-0000-7000-8000-000000000002");
    private static readonly Guid Flat = Guid.Parse("0199e3a0-0000-7000-8000-000000000003");
    private static readonly Guid Brighton = Guid.Parse("0199e3a0-0000-7000-8000-0000000000b1");
    private static readonly LocalDate Saturday = new(2026, 11, 7);

    [Fact]
    public void TheBase_IsTheRoutinesOnItsDates_AndHomeOtherwise()
    {
        var context = BrightonWeekend(flatToGymKnown: false);

        Assert.Equal(Flat, context.BaseOn(Saturday));
        Assert.Equal(Flat, context.BaseOn(Saturday.PlusDays(1)));
        Assert.Equal(Home, context.BaseOn(Saturday.PlusDays(-1)));
        Assert.Equal(Home, context.BaseOn(Saturday.PlusDays(2)));
    }

    // A routine with no base of its own uses home.
    [Fact]
    public void ARoutineWithoutABase_UsesHome()
    {
        var context = BrightonWeekend(flatToGymKnown: false) with { RoutineBases = new Dictionary<Guid, Guid>() };

        Assert.Equal(Home, context.BaseOn(Saturday));
    }

    // P1: travel to the gym from the flat can't be worked out, so a gym session every Saturday can't be saved as it is:
    // said with the first date it is needed, never guessed as zero.
    [Fact]
    public void TravelFromABaseWithNoTravelTime_IsAProblem_NamingTheDate()
    {
        var gym = Saturdays("Gym", at: Gym);

        var problem = Assert.Single(ActivityCheck.Check(gym, [], BrightonWeekend(flatToGymKnown: false)).Problems);

        Assert.Equal(CheckAspect.Location, problem.Aspect);
        Assert.StartsWith("On Sat 7 Nov 2026 your base is Brighton flat, and there's no travel time from Brighton flat to Town gym", problem.Message);
    }

    // With the travel times known, the journey there on the Brighton Saturday is the flat's hour, not home's 25 minutes:
    // a gym session at 18:00 leaves at 17:00 and runs into dinner at the flat until 17:30.
    [Fact]
    public void OnTheBasesDates_TravelIsFromTheBase()
    {
        var gym = Saturdays("Gym", at: Gym);
        var dinner = OneOff("Dinner", Saturday, new LocalTime(16, 30), minutes: 60, at: Flat);

        var clash = Assert.Single(ActivityCheck.Check(gym, [dinner], BrightonWeekend(flatToGymKnown: true)).Clashes);

        Assert.Equal((FootprintPart.TravelOut, "Dinner"), (clash.CandidatePart, clash.Other.Label));
        Assert.Equal(Instant.FromUtc(2026, 11, 7, 17, 0), clash.Overlap.Start);
    }

    // An activity at the base itself needs no travel there, even with no travel time between home and the flat.
    [Fact]
    public void AnActivityAtTheBase_NeedsNoTravelThere()
    {
        var weekly = Saturdays("Seafront yoga", at: Flat);
        var yoga = weekly with { Series = weekly.Series.OnlyOn(date => date >= Saturday && date <= Saturday.PlusDays(1)) };

        var result = ActivityCheck.Check(yoga, [], BrightonWeekend(flatToGymKnown: false));

        Assert.Empty(result.Problems);
    }

    // CAL-4: one occurrence moved is checked from the base of the day it lands on.
    [Fact]
    public void AnOccurrenceMovedOntoTheBasesDates_IsCheckedFromTheBase()
    {
        var gym = Saturdays("Gym", at: Gym);
        var moved = gym with { Series = gym.Series.MoveOccurrence(new LocalDate(2026, 10, 31), new LocalDateTime(2026, 11, 8, 10, 0), Duration.FromMinutes(50)) };

        var problem = Assert.Single(ActivityCheck.CheckOccurrence(moved, new LocalDate(2026, 10, 31), [], BrightonWeekend(flatToGymKnown: false)).Problems);

        Assert.StartsWith("On Sun 8 Nov 2026 your base is Brighton flat", problem.Message);
    }

    // The home page and calendar draw each footprint from its own day's base: 17:00 on the Brighton Saturday (an hour
    // from the flat), 17:35 the Saturday after (25 minutes from home).
    [Fact]
    public void TheAgenda_DrawsEachFootprintFromItsDaysBase()
    {
        var gym = Saturdays("Gym", at: Gym);

        var items = Agenda.Between([gym], BrightonWeekend(flatToGymKnown: true), Saturday, Saturday.PlusDays(7));

        Assert.Equal(
            [Instant.FromUtc(2026, 11, 7, 17, 0), Instant.FromUtc(2026, 11, 14, 17, 35)],
            items.Select(item => item.Footprint!.Whole.Start));
    }

    // ... and the trip says where it leaves from: the flat on the Brighton Saturday, home the Saturday after (scheduling
    // review, Phase 6b: it said "Leave home" on the Brighton days too).
    [Fact]
    public void TheTrip_LeavesFromEachDaysBase()
    {
        var gym = Saturdays("Gym", at: Gym);

        var items = Agenda.Between([gym], BrightonWeekend(flatToGymKnown: true), Saturday, Saturday.PlusDays(7));

        Assert.Equal(["Leave Brighton flat 17:00, back 19:50", "Leave home 17:35, back 19:35"], items.Select(item => Agenda.Trip(item)));
    }

    // ... and says so when travel from that day's base can't be worked out (P1).
    [Fact]
    public void TheAgenda_SaysWhenTravelFromTheBaseIsUnknown()
    {
        var gym = Saturdays("Gym", at: Gym);

        var items = Agenda.Between([gym], BrightonWeekend(flatToGymKnown: false), Saturday, Saturday.PlusDays(7));

        Assert.StartsWith("Travel isn't counted, because your base then is Brighton flat, and there's no travel time from Brighton flat to Town gym", items[0].Note);
        Assert.Null(items[1].Note);
    }

    // T15, SCH-5: planning the week of Monday 2 November (on Monday 26 October) with the Brighton weekend on Saturday and
    // Sunday. The town gym can't be reached from the flat, so a gym session a day goes on Monday to Friday, and the
    // weekend days say why; runs (no particular place) are still suggested every day, the weekend too.
    [Fact]
    public void T15_TheHomeGymIsOutOfReach_ButRunsAreStillSuggested()
    {
        var gym = Goal("Gym", sessions: 7, at: Gym);
        var run = Goal("Run", sessions: 7, at: null);

        var plan = Planner.Plan(WeekOf([gym, run], BrightonWeekend(flatToGymKnown: false)));

        Assert.Equal(
            [new(2026, 11, 2), new(2026, 11, 3), new(2026, 11, 4), new(2026, 11, 5), new(2026, 11, 6)],
            plan.Suggestions.Where(suggestion => suggestion.Goal == gym).Select(suggestion => suggestion.Time.Start.InZone(London).Date).Order());
        Assert.Equal(7, plan.Suggestions.Count(suggestion => suggestion.Goal == run));
        var shortfall = Assert.Single(plan.Shortfalls);
        Assert.Equal((gym, 2), (shortfall.Goal, shortfall.Missing));
        Assert.Equal([Saturday, Saturday.PlusDays(1)], shortfall.Days.Select(day => day.Date));
        Assert.All(shortfall.Days, day => Assert.StartsWith("Your base then is Brighton flat, and there's no travel time from Brighton flat to Town gym", day.Reason));
    }

    // With travel from the flat known, the Brighton Saturday's gym session is planned with the flat's hour each way.
    [Fact]
    public void ThePlanner_UsesTravelFromTheDaysBase()
    {
        var gym = Goal("Gym", sessions: 7, at: Gym);

        var plan = Planner.Plan(WeekOf([gym], BrightonWeekend(flatToGymKnown: true)));

        var saturday = Assert.Single(plan.Suggestions, suggestion => suggestion.Time.Start.InZone(London).Date == Saturday);
        Assert.Equal(Duration.FromMinutes(60), saturday.Time.Start - saturday.Footprint.Whole.Start);
        var monday = Assert.Single(plan.Suggestions, suggestion => suggestion.Time.Start.InZone(London).Date == Saturday.PlusDays(-5));
        Assert.Equal(Duration.FromMinutes(25), monday.Time.Start - monday.Footprint.Whole.Start);
    }

    // A goal is only blocked outright when no day of the week can reach it: a whole week in Brighton.
    [Fact]
    public void AGoalNoDayCanReach_IsBlocked_SayingWhy()
    {
        var gym = Goal("Gym", sessions: 3, at: Gym);
        var wholeWeek = BrightonWeekend(flatToGymKnown: false) with
        {
            Routines = new RoutineCalendar(
                new Dictionary<Guid, WeekCycle> { [Brighton] = new(weeks: 1, anchor: Today) },
                usual: null,
                [new RoutineDates(Brighton, new DateInterval(new(2026, 11, 2), new(2026, 11, 8)))]),
        };

        var shortfall = Assert.Single(Planner.Plan(WeekOf([gym], wholeWeek)).Shortfalls);

        Assert.StartsWith("Your base then is Brighton flat, and there's no travel time from Brighton flat to Town gym", shortfall.Blocked);
    }

    // Busy time is drawn from each day's base too. On the Brighton Saturday: breakfast at the flat until 08:25, a gym
    // class from 10:00, and an afternoon out from 12:00. From the flat the class takes up from 09:00, leaving 35 minutes
    // after breakfast, too short for a 50-minute run (from home it would be 09:35, room for one at 08:30), so a run a day
    // has none that Saturday, which says why.
    [Fact]
    public void BusyTime_IsDrawnFromEachDaysBase()
    {
        PlannedActivity[] saturday =
        [
            OneOff("Breakfast", Saturday, new LocalTime(7, 30), minutes: 55, at: Flat),
            OneOff("Gym class", Saturday, new LocalTime(10, 0), minutes: 60, at: Gym),
            OneOff("Afternoon out", Saturday, new LocalTime(12, 0), minutes: 660, at: Flat),
        ];
        var run = Goal("Run", sessions: 7, at: null);

        var plan = Planner.Plan(WeekOf([run], BrightonWeekend(flatToGymKnown: true), saturday));

        Assert.DoesNotContain(plan.Suggestions, suggestion => suggestion.Time.Start.InZone(London).Date == Saturday);
        Assert.Equal(Saturday, Assert.Single(Assert.Single(plan.Shortfalls).Days).Date);
    }

    private static PlanRequest WeekOf(PlanGoal[] goals, PlanningContext context, PlannedActivity[]? activities = null) =>
        new(new DateInterval(new(2026, 11, 2), new(2026, 11, 8)), Instant.FromUtc(2026, 10, 26, 12, 0), London, activities ?? [], goals,
            new SleepWindow(new LocalTime(23, 0), new LocalTime(7, 0), Duration.FromMinutes(30), Duration.FromMinutes(30)),
            new RestRules(Duration.FromHours(24), 1, new HashSet<IsoDayOfWeek>()),
            context);

    private static PlanGoal Goal(string title, int sessions, Guid? at, IsoDayOfWeek[]? days = null) =>
        new(Guid.NewGuid(), title, sessions, Duration.FromMinutes(50), Buffers.None, at, false, 1, new HashSet<IsoDayOfWeek>(days ?? []), TimeOfDay.Any);

    private static PlanningContext BrightonWeekend(bool flatToGymKnown)
    {
        var legs = new Dictionary<Leg, Duration>
        {
            [new Leg(Home, Gym)] = Duration.FromMinutes(25),
            [new Leg(Gym, Home)] = Duration.FromMinutes(45),
        };
        if (flatToGymKnown)
        {
            legs[new Leg(Flat, Gym)] = Duration.FromMinutes(60);
            legs[new Leg(Gym, Flat)] = Duration.FromMinutes(60);
        }

        return new PlanningContext(Today, Home, new TravelTimes(legs), new Dictionary<Guid, Place>
        {
            [Home] = new("Home", OpeningHours.Always),
            [Gym] = new("Town gym", OpeningHours.Always),
            [Flat] = new("Brighton flat", OpeningHours.Always),
        })
        {
            Routines = new RoutineCalendar(
                new Dictionary<Guid, WeekCycle> { [Brighton] = new(weeks: 1, anchor: Today) },
                usual: null,
                [new RoutineDates(Brighton, new DateInterval(Saturday, Saturday.PlusDays(1)))]),
            RoutineBases = new Dictionary<Guid, Guid> { [Brighton] = Flat },
        };
    }

    // Every Saturday at 18:00 for 50 minutes from Saturday 24 October, whatever the routine.
    private static PlannedActivity Saturdays(string title, Guid at) =>
        new(Guid.NewGuid(), title,
            new Series(new LocalDate(2026, 10, 24), new LocalTime(18, 0), Duration.FromMinutes(50), London, RecurrenceRule.Weekly([IsoDayOfWeek.Saturday])),
            Buffers.None, at);

    private static PlannedActivity OneOff(string title, LocalDate date, LocalTime start, int minutes, Guid at) =>
        new(Guid.NewGuid(), title, new Series(date, start, Duration.FromMinutes(minutes), London, RecurrenceRule.Daily().EndingAfter(1)), Buffers.None, at);
}
