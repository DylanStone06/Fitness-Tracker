using FitnessTracker.Domain.Footprints;
using FitnessTracker.Domain.Locations;
using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Domain.Time;
using NodaTime;

namespace FitnessTracker.Domain.Tests.Scheduling;

// SCH-2, SCH-3, SCH-5, SCH-7 and requirements §7.4: "Plan my week" finds times for the sessions weekly goals (GOL-1)
// still need, around everything already planned and sleep, never breaking a hard rule, best first, and says why when
// one won't fit. The week is Monday 2 to Sunday 8 November 2026 in London (no clock change); it is planned on Monday
// 26 October, so no day is over. Sleep is 23:00 to 07:00, with 30 minutes to wind down and 30 to get going.
public class PlannerTests
{
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly DateInterval Week = new(new LocalDate(2026, 11, 2), new LocalDate(2026, 11, 8));
    private static readonly Instant PlannedOn = Instant.FromUtc(2026, 10, 26, 12, 0);
    private static readonly Guid Home = Guid.Parse("0199e3a0-0000-7000-8000-000000000001");
    private static readonly Guid Gym = Guid.Parse("0199e3a0-0000-7000-8000-000000000002");
    private static readonly Guid Office = Guid.Parse("0199e3a0-0000-7000-8000-000000000003");
    private static readonly Guid Pool = Guid.Parse("0199e3a0-0000-7000-8000-000000000004");

    [Fact]
    public void AnEmptyWeek_GetsEverySession()
    {
        var plan = Planner.Plan(Request([Goal("Gym", sessions: 3)]));

        Assert.Equal(3, plan.Suggestions.Count);
        Assert.Empty(plan.Shortfalls);
    }

    // SCH-1: every suggestion passes the one check on save, as a one-off at that time, against everything else
    // planned and the other suggestions: no problems, no clashes.
    [Fact]
    public void EverySuggestion_PassesTheCheckOnSave()
    {
        var work = WorkWeekdays();
        var goals = new[] { Goal("Gym", sessions: 3, at: Gym, buffers: new(Duration.FromMinutes(10), Duration.FromMinutes(10))), Goal("Run", sessions: 2, minutes: 40) };

        var plan = Planner.Plan(Request(goals, [work]));

        Assert.Equal(5, plan.Suggestions.Count);
        var asActivities = plan.Suggestions.Select(OneOffFor).ToList();
        foreach (var suggestion in asActivities)
        {
            var result = ActivityCheck.Check(suggestion, asActivities.Append(work), Context());
            Assert.Empty(result.Problems);
            Assert.Empty(result.Clashes);
        }
    }

    // §7.4: busy time includes sleep, with winding down and getting going (T12: sleep is never cut into).
    [Fact]
    public void NoSuggestion_CutsIntoSleep()
    {
        var plan = Planner.Plan(Request([Goal("Gym", sessions: 7)]));

        Assert.Equal(7, plan.Suggestions.Count);
        Assert.All(plan.Suggestions, suggestion =>
        {
            var start = suggestion.Footprint.Whole.Start.InZone(London).TimeOfDay;
            var end = suggestion.Footprint.Whole.End.InZone(London).TimeOfDay;
            Assert.True(start >= new LocalTime(7, 30), $"starts at {start}");
            Assert.True(end <= new LocalTime(22, 30), $"ends at {end}");
        });
    }

    // T9 and H1: around an office day, with its commute (30 minutes each way), and never on top of anything.
    [Fact]
    public void Suggestions_FitAroundWorkAndItsCommute()
    {
        var work = WorkWeekdays();

        var plan = Planner.Plan(Request([Goal("Gym", sessions: 5, at: Gym)], [work]));

        Assert.Equal(5, plan.Suggestions.Count);
        var workFootprints = work.Series.OccurrencesBetween(Week.Start, Week.End)
            .Select(occurrence => Footprint.Around(occurrence.Time, Buffers.None, new(Duration.FromMinutes(30), Duration.FromMinutes(30))).Whole);
        Assert.All(plan.Suggestions, suggestion => Assert.DoesNotContain(workFootprints, work => work.Start < suggestion.Footprint.Whole.End && suggestion.Footprint.Whole.Start < work.End));
    }

    // §7.3: preferred days and time of day come first when they fit.
    [Fact]
    public void PreferredDaysAndTime_AreUsedWhenTheyFit()
    {
        var goal = Goal("Gym", sessions: 3, days: [IsoDayOfWeek.Monday, IsoDayOfWeek.Wednesday, IsoDayOfWeek.Friday], time: TimeOfDay.Evening);

        var plan = Planner.Plan(Request([goal]));

        Assert.Equal(
            [IsoDayOfWeek.Monday, IsoDayOfWeek.Wednesday, IsoDayOfWeek.Friday],
            plan.Suggestions.Select(suggestion => suggestion.Time.Start.InZone(London).DayOfWeek).Order());
        Assert.All(plan.Suggestions, suggestion => Assert.InRange(suggestion.Time.Start.InZone(London).Hour, 17, 21));
        Assert.Contains("Your preferred day", plan.Suggestions[0].Reasons);
        Assert.Contains("In the evening, as you prefer", plan.Suggestions[0].Reasons);
    }

    // §7.3 spread: with no preferences, sessions of one goal are kept apart rather than on days next to each other.
    [Fact]
    public void SessionsOfOneGoal_AreSpreadThroughTheWeek()
    {
        var plan = Planner.Plan(Request([Goal("Run", sessions: 3, minutes: 40)]));

        var days = plan.Suggestions.Select(suggestion => suggestion.Time.Start.InZone(London).Date).Order().ToList();
        Assert.Equal(3, days.Distinct().Count());
        Assert.True(Period.Between(days[0], days[1], PeriodUnits.Days).Days >= 2);
        Assert.True(Period.Between(days[1], days[2], PeriodUnits.Days).Days >= 2);
    }

    // SCH-4: a day said no to for a goal gets none of its sessions; another day does. (Saying no to just the time gave
    // the next quarter hour, found in the browser: no real choice.) Another time that day is "Change".
    [Fact]
    public void ARejectedDay_GetsNoSessionOfThatGoal()
    {
        var goal = Goal("Gym", sessions: 3);
        var first = Planner.Plan(Request([goal]));
        var rejected = first.Suggestions[0].Time.Start.InZone(London).Date;

        var second = Planner.Plan(Request([goal]) with { Rejected = [new RejectedDay(goal.Id, rejected)] });

        Assert.Equal(3, second.Suggestions.Count);
        Assert.DoesNotContain(second.Suggestions, suggestion => suggestion.Time.Start.InZone(London).Date == rejected);
    }

    // ... and when nothing else fits, that day's reason says so.
    [Fact]
    public void ARejectedDay_IsGivenAsTheReason_WhenNothingElseFits()
    {
        var goal = Goal("Gym", sessions: 7);

        var plan = Planner.Plan(Request([goal]) with { Rejected = [new RejectedDay(goal.Id, new LocalDate(2026, 11, 4))] });

        Assert.Equal("You said no to this day", Assert.Single(Assert.Single(plan.Shortfalls).Days).Reason);
    }

    // T22, SCH-7: the same inputs always give the same suggestions, whatever order they arrive in.
    [Fact]
    public void TheSameWeekPlannedTwice_GivesTheSameSuggestions()
    {
        var goals = new[] { Goal("Gym", sessions: 3, at: Gym), Goal("Run", sessions: 2, minutes: 40), Goal("Swim", sessions: 1, at: Pool) };
        var activities = new[] { WorkWeekdays(), OneOff("Dinner", new LocalDate(2026, 11, 4), new LocalTime(19, 0), 120) };

        var first = Planner.Plan(Request(goals, activities));
        var second = Planner.Plan(Request(goals.Reverse().ToArray(), activities.Reverse().ToArray()));

        Assert.Equal(6, first.Suggestions.Count);
        Assert.Equal(Describe(first), Describe(second));
    }

    // Sessions already planned for a goal count towards it: one gym session on the calendar leaves two to find.
    [Fact]
    public void SessionsAlreadyPlanned_CountTowardsTheGoal()
    {
        var goal = Goal("Gym", sessions: 3);
        var planned = OneOff("Gym", new LocalDate(2026, 11, 3), new LocalTime(18, 0), 60) with { GoalId = goal.Id };

        var plan = Planner.Plan(Request([goal], [planned]));

        Assert.Equal(1, plan.AlreadyPlanned[goal.Id]);
        Assert.Equal(2, plan.Suggestions.Count);
    }

    // Nothing is suggested for a time that has passed: planned on Wednesday 4 November at 12:00.
    [Fact]
    public void NothingIsSuggestedInThePast()
    {
        var now = Instant.FromUtc(2026, 11, 4, 12, 0);

        var plan = Planner.Plan(Request([Goal("Gym", sessions: 3)]) with { Now = now });

        Assert.Equal(3, plan.Suggestions.Count);
        Assert.All(plan.Suggestions, suggestion => Assert.True(suggestion.Footprint.Whole.Start >= now));
    }

    // H4: only when the place is open; the day it's closed says so. Town gym opens 06:00-09:00 on weekdays only.
    [Fact]
    public void OpeningHours_AreKept_AndAClosedDayIsExplained()
    {
        var weekdayMornings = new OpeningHours(new[] { IsoDayOfWeek.Monday, IsoDayOfWeek.Tuesday, IsoDayOfWeek.Wednesday, IsoDayOfWeek.Thursday, IsoDayOfWeek.Friday }
            .ToDictionary(day => day, _ => new OpenTimes(new LocalTime(6, 0), new LocalTime(9, 0))));

        var plan = Planner.Plan(Request([Goal("Gym", sessions: 7, at: Gym)]) with { Context = Context(gymHours: weekdayMornings) });

        Assert.Equal(5, plan.Suggestions.Count);
        Assert.All(plan.Suggestions, suggestion => Assert.Null(weekdayMornings.ProblemWith(suggestion.Time, London)));
        var shortfall = Assert.Single(plan.Shortfalls);
        Assert.Equal(2, shortfall.Missing);
        Assert.Equal("Town gym is closed that day", shortfall.Days.Single(day => day.Date == new LocalDate(2026, 11, 7)).Reason);
    }

    // T13, H5: demanding sessions at least the set time apart (here 48 hours, start to start), and the rule is named.
    // Greedy, best first (§7.4): Monday, then Thursday (further from Monday than Wednesday), then Sunday, after which
    // no day is 48 hours from all three. Four would fit (Monday, Wednesday, Friday, Sunday): §7.4 says the method
    // isn't guaranteed to find the best possible week.
    [Fact]
    public void DemandingSessions_AreKeptApart_AndTheRuleIsNamed()
    {
        var goal = Goal("Intervals", sessions: 7, minutes: 45, demanding: true);

        var plan = Planner.Plan(Request([goal]) with { Rest = new RestRules(Duration.FromHours(48), 1, new HashSet<IsoDayOfWeek>()) });

        var starts = plan.Suggestions.Select(suggestion => suggestion.Time.Start).Order().ToList();
        Assert.Equal(3, starts.Count);
        Assert.All(starts.Zip(starts.Skip(1)), pair => Assert.True(pair.Second - pair.First >= Duration.FromHours(48)));
        Assert.Contains(Assert.Single(plan.Shortfalls).Days, day => day.Reason.Contains("at least 48 hours apart"));
    }

    // H7: no demanding session on a recovery day, and that is the day's reason.
    [Fact]
    public void ARecoveryDay_GetsNoDemandingSession()
    {
        var goal = Goal("Intervals", sessions: 7, minutes: 45, demanding: true);
        var rest = new RestRules(Duration.Zero, 1, new HashSet<IsoDayOfWeek> { IsoDayOfWeek.Wednesday });

        var plan = Planner.Plan(Request([goal]) with { Rest = rest });

        Assert.DoesNotContain(plan.Suggestions, suggestion => suggestion.Time.Start.InZone(London).DayOfWeek == IsoDayOfWeek.Wednesday);
        Assert.Equal("A recovery day, and Intervals is demanding", Assert.Single(plan.Shortfalls).Days.Single().Reason);
    }

    // H6: at most the set number of demanding sessions in a day, counting ones already planned.
    [Fact]
    public void ADayWithItsDemandingSessionsAlready_GetsNoMore()
    {
        var goal = Goal("Intervals", sessions: 7, minutes: 45, demanding: true);
        var hardRide = OneOff("Hard ride", new LocalDate(2026, 11, 5), new LocalTime(7, 30), 90) with { IsDemanding = true };

        var plan = Planner.Plan(Request([goal], [hardRide]) with { Rest = new RestRules(Duration.Zero, 1, new HashSet<IsoDayOfWeek>()) });

        Assert.DoesNotContain(plan.Suggestions, suggestion => suggestion.Time.Start.InZone(London).DayOfWeek == IsoDayOfWeek.Thursday);
        Assert.Equal("It already has 1 demanding session, the most for a day", Assert.Single(plan.Shortfalls).Days.Single().Reason);
    }

    // T14, SCH-5: a busy day says how long its longest free time is and how long the session needs. Each day here is
    // taken up but for 85 minutes from 17:30; the gym needs 130, as in T1 (25 there, 50, 10 changing, 45 back).
    [Fact]
    public void ABusyDay_SaysItsLongestFreeTime_AndWhatTheSessionNeeds()
    {
        var gym = Goal("Gym", sessions: 1, at: Gym, minutes: 50, buffers: new(Duration.Zero, Duration.FromMinutes(10)));
        var busyDays = new[]
        {
            Daily("Day", new LocalTime(7, 30), Duration.FromMinutes(600)),
            Daily("Evening", new LocalTime(18, 55), Duration.FromMinutes(215)),
        };

        var plan = Planner.Plan(Request([gym], busyDays));

        Assert.Empty(plan.Suggestions);
        var tuesday = Assert.Single(plan.Shortfalls).Days.Single(day => day.Date == new LocalDate(2026, 11, 3));
        Assert.Equal("The longest free time is 1 h 25 min; Gym needs 2 h 10 min with getting ready, travel and changing", tuesday.Reason);
    }

    // SCH-5: when a session won't fit, a shorter one that would is offered, with its time.
    [Fact]
    public void WhenASessionWontFit_AShorterOneIsOffered()
    {
        var gym = Goal("Gym", sessions: 1, minutes: 90);
        var busyDays = new[]
        {
            Daily("Day", new LocalTime(7, 30), Duration.FromMinutes(600)),
            Daily("Evening", new LocalTime(18, 30), Duration.FromMinutes(240)),
        };

        var plan = Planner.Plan(Request([gym], busyDays));

        Assert.Equal("A 60-minute session would fit: Mon 2 Nov 2026 at 17:30", Assert.Single(plan.Shortfalls).Shorter);
    }

    // P1: travel that can't be worked out is never guessed; the goal isn't planned, and the reason is given.
    [Fact]
    public void AGoalWhoseTravelIsUnknown_IsNotPlanned_AndSaysWhy()
    {
        var plan = Planner.Plan(Request([Goal("Swim", sessions: 2, at: Pool)]) with { Context = Context(withPoolTravel: false) });

        Assert.Empty(plan.Suggestions);
        Assert.Equal("There's no travel time from Home to Town pool, or from Town pool to Home", Assert.Single(plan.Shortfalls).Blocked);
    }

    // §7.4: higher-priority goals are fitted first; with time for only one session, the high one gets it.
    [Fact]
    public void HigherPriorityGoals_AreFittedFirst()
    {
        var oneSlot = new[]
        {
            Daily("Day", new LocalTime(7, 30), Duration.FromMinutes(600)),
            Daily("Evening", new LocalTime(18, 30), Duration.FromMinutes(240)),
        };
        var low = Goal("Stretching", sessions: 7, minutes: 60, priority: 0);
        var high = Goal("Run", sessions: 7, minutes: 60, priority: 2);

        var plan = Planner.Plan(Request([low, high], oneSlot));

        Assert.Equal(7, plan.Suggestions.Count);
        Assert.All(plan.Suggestions, suggestion => Assert.Equal("Run", suggestion.Goal.Title));
    }

    // T2: weekly targets that can't all fit around work and its commute: what fits is suggested, the rest explained
    // per day, nothing overlaps and sleep is intact.
    [Fact]
    public void TargetsThatDontFitAroundWork_AreExplainedPerDay_WithNoOverlaps()
    {
        var work = WorkWeekdays();
        var gym = Goal("Gym", sessions: 7, at: Gym, minutes: 90, buffers: new(Duration.FromMinutes(10), Duration.FromMinutes(10)));
        var longWorkDays = new[] { work, Daily("Evening class", new LocalTime(18, 30), Duration.FromMinutes(180)) };

        var plan = Planner.Plan(Request([gym], longWorkDays));

        var shortfall = Assert.Single(plan.Shortfalls);
        Assert.Equal(7, plan.Suggestions.Count + shortfall.Missing);
        Assert.NotEmpty(shortfall.Days);
        var footprints = plan.Suggestions.Select(suggestion => suggestion.Footprint.Whole).ToList();
        Assert.All(footprints, a => Assert.DoesNotContain(footprints, b => a != b && a.Start < b.End && b.Start < a.End));
    }

    private static string Describe(WeekPlan plan) =>
        string.Join("\n", plan.Suggestions.Select(suggestion => $"{suggestion.Goal.Title} {suggestion.Time}"))
        + "\n" + string.Join("\n", plan.Shortfalls.Select(shortfall => $"{shortfall.Goal.Title} {shortfall.Missing} {string.Join("; ", shortfall.Days.Select(day => $"{day.Date} {day.Reason}"))}"));

    private static PlannedActivity OneOffFor(Suggestion suggestion)
    {
        var start = suggestion.Time.Start.InZone(London).LocalDateTime;
        return new PlannedActivity(Guid.NewGuid(), suggestion.Goal.Title,
            new Series(start.Date, start.TimeOfDay, suggestion.Goal.Length, London, RecurrenceRule.Daily().EndingAfter(1)),
            suggestion.Goal.Buffers, suggestion.Goal.LocationId);
    }

    private static PlanRequest Request(PlanGoal[] goals, PlannedActivity[]? activities = null) =>
        new(Week, PlannedOn, London, activities ?? [], goals,
            new SleepWindow(new LocalTime(23, 0), new LocalTime(7, 0), Duration.FromMinutes(30), Duration.FromMinutes(30)),
            new RestRules(Duration.FromHours(24), 1, new HashSet<IsoDayOfWeek>()),
            Context());

    private static PlanGoal Goal(
        string title, int sessions, int minutes = 60, Guid? at = null, Buffers? buffers = null, bool demanding = false, int priority = 1,
        IsoDayOfWeek[]? days = null, TimeOfDay time = TimeOfDay.Any) =>
        new(Guid.NewGuid(), title, sessions, Duration.FromMinutes(minutes), buffers ?? Buffers.None, at, demanding, priority,
            new HashSet<IsoDayOfWeek>(days ?? []), time);

    // Work at the office, Monday to Friday 09:00 for 8 hours 30, a 30-minute commute each way.
    private static PlannedActivity WorkWeekdays() =>
        new(Guid.Parse("0199e3a0-0000-7000-8000-0000000000aa"), "Work",
            new Series(Week.Start, new LocalTime(9, 0), Duration.FromMinutes(510), London,
                RecurrenceRule.Weekly([IsoDayOfWeek.Monday, IsoDayOfWeek.Tuesday, IsoDayOfWeek.Wednesday, IsoDayOfWeek.Thursday, IsoDayOfWeek.Friday])),
            Buffers.None, Office);

    private static PlannedActivity Daily(string title, LocalTime start, Duration length) =>
        new(Guid.NewGuid(), title, new Series(Week.Start.PlusDays(-7), start, length, London, RecurrenceRule.Daily()), Buffers.None, null);

    private static PlannedActivity OneOff(string title, LocalDate date, LocalTime start, int minutes) =>
        new(Guid.NewGuid(), title, new Series(date, start, Duration.FromMinutes(minutes), London, RecurrenceRule.Daily().EndingAfter(1)), Buffers.None, null);

    private static PlanningContext Context(OpeningHours? gymHours = null, bool withPoolTravel = true)
    {
        var legs = new Dictionary<Leg, Duration>
        {
            [new Leg(Home, Gym)] = Duration.FromMinutes(25),
            [new Leg(Gym, Home)] = Duration.FromMinutes(45),
            [new Leg(Home, Office)] = Duration.FromMinutes(30),
            [new Leg(Office, Home)] = Duration.FromMinutes(30),
        };
        if (withPoolTravel)
        {
            legs[new Leg(Home, Pool)] = Duration.FromMinutes(15);
            legs[new Leg(Pool, Home)] = Duration.FromMinutes(15);
        }

        return new PlanningContext(new LocalDate(2026, 10, 26), Home, new TravelTimes(legs), new Dictionary<Guid, Place>
        {
            [Home] = new("Home", OpeningHours.Always),
            [Gym] = new("Town gym", gymHours ?? OpeningHours.Always),
            [Office] = new("Office", OpeningHours.Always),
            [Pool] = new("Town pool", OpeningHours.Always),
        });
    }
}
