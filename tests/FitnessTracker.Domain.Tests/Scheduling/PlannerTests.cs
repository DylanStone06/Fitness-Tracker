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

    // SCH-1, H5-H7: with rest rules (48 hours apart, Thursday a recovery day), every demanding suggestion passes the
    // check on save as a demanding one-off, against the others (scheduling review, Phase 6: the test above has no rest
    // rules, so the planner and the check were never compared on them).
    [Fact]
    public void EveryDemandingSuggestion_PassesTheRestRulesOnSave()
    {
        var rest = new RestRules(Duration.FromHours(48), 1, new HashSet<IsoDayOfWeek> { IsoDayOfWeek.Thursday });
        var context = Context() with { Rest = rest };

        var plan = Planner.Plan(Request([Goal("Intervals", sessions: 3, minutes: 45, demanding: true)]) with { Rest = rest, Context = context });

        Assert.Equal(3, plan.Suggestions.Count);
        var asActivities = plan.Suggestions.Select(OneOffFor).ToList();
        Assert.All(asActivities, activity => Assert.True(activity.IsDemanding));
        foreach (var suggestion in asActivities)
        {
            Assert.Empty(ActivityCheck.Check(suggestion, asActivities, context).Problems);
        }
    }

    // §7.4: busy time includes sleep, with winding down and getting going (T12: sleep is never cut into).
    [Fact]
    public void NoSuggestion_CutsIntoSleep()
    {
        var plan = Planner.Plan(Request([Goal("Gym", sessions: 7)]));

        Assert.Equal(7, plan.Suggestions.Count);
        // Moments, not clock times, so a footprint crossing midnight can't slip through (review L-5). November: GMT = UTC.
        Assert.All(plan.Suggestions, suggestion =>
        {
            var day = suggestion.Time.Start.InUtc().Date;
            Assert.True(suggestion.Footprint.Whole.Start >= Instant.FromUtc(day.Year, day.Month, day.Day, 7, 30));
            Assert.True(suggestion.Footprint.Whole.End <= Instant.FromUtc(day.Year, day.Month, day.Day, 22, 30));
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

        // Written as other lengths are, so a long one reads well ("11 h 15 min", not "675-minute": break-ui, Phase 6).
        Assert.Equal("A session of 1 h would fit: Mon 2 Nov 2026 at 17:30", Assert.Single(plan.Shortfalls).Shorter);
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

    // H5, T13 (scheduling review H-1): a demanding session four days before the week still counts when the rule is 96
    // hours. The ride is on Thursday 29 October at 18:00 GMT; 96 hours later is Monday 2 November 18:00, the best time
    // from then on (before, Monday 08:30 was suggested, 86.5 hours after it).
    [Fact]
    public void ADemandingSessionBeforeTheWeek_StillKeepsItsRestGap()
    {
        var goal = Goal("Intervals", sessions: 1, minutes: 45, demanding: true);
        var hardRide = OneOff("Hard ride", new LocalDate(2026, 10, 29), new LocalTime(18, 0), 60) with { IsDemanding = true };

        var plan = Planner.Plan(Request([goal], [hardRide]) with { Rest = new RestRules(Duration.FromHours(96), 1, new HashSet<IsoDayOfWeek>()) });

        Assert.Equal(Instant.FromUtc(2026, 11, 2, 18, 0), Assert.Single(plan.Suggestions).Time.Start);
        Assert.Equal(new DateInterval(new LocalDate(2026, 10, 28), new LocalDate(2026, 11, 13)), Planner.DatesToLoadFor(Week, Duration.FromHours(96)));
    }

    // SCH-5 (review M-1): each day is free only from 17:40 to 19:55 (135 min). The gym needs 130 from 25 minutes before a
    // quarter-hour start, so its activity would have to start at 18:05 or 18:10: nothing fits, and the reason says why
    // rather than reading as if it fitted.
    [Fact]
    public void AGapLongEnough_ButOffTheQuarterHour_SaysSo()
    {
        var gym = Goal("Gym", sessions: 1, at: Gym, minutes: 50, buffers: new(Duration.Zero, Duration.FromMinutes(10)));
        var busyDays = new[]
        {
            Daily("Day", new LocalTime(7, 30), Duration.FromMinutes(610)),
            Daily("Evening", new LocalTime(19, 55), Duration.FromMinutes(185)),
        };

        var plan = Planner.Plan(Request([gym], busyDays));

        Assert.Empty(plan.Suggestions);
        var tuesday = Assert.Single(plan.Shortfalls).Days.Single(day => day.Date == new LocalDate(2026, 11, 3));
        Assert.Equal(
            "The longest free time, 17:40 to 19:55, is 2 h 15 min, but with sessions starting on the quarter hour, Gym " +
            "(2 h 10 min with getting ready, travel and changing) doesn't fit in it",
            tuesday.Reason);
    }

    // SCH-5 (review M-2): planned on Friday 6 November at 12:00, five gym sessions get Friday, Saturday and Sunday (one a
    // day); the two left over are still explained.
    [Fact]
    public void SessionsThatDontFitInWhatsLeftOfTheWeek_AreStillExplained()
    {
        var plan = Planner.Plan(Request([Goal("Gym", sessions: 5)]) with { Now = Instant.FromUtc(2026, 11, 6, 12, 0) });

        Assert.Equal(3, plan.Suggestions.Count);
        var shortfall = Assert.Single(plan.Shortfalls);
        Assert.Equal(2, shortfall.Missing);
        Assert.Empty(shortfall.Days);
        Assert.Equal("Every day left this week already has a Gym session (one a day)", shortfall.Note);
    }

    // SCH-5 (review M-4): one free hour a day (17:30-18:30). Gym (high priority, 90 min) can't fit; Stretching (low, 60
    // min) then takes every free hour, so no shorter Gym session is offered at a time it was given.
    [Fact]
    public void AShorterSessionOffered_IsNotATimeGivenToAnotherGoal()
    {
        var oneSlot = new[] { Daily("Day", new LocalTime(7, 30), Duration.FromMinutes(600)), Daily("Evening", new LocalTime(18, 30), Duration.FromMinutes(240)) };
        var gym = Goal("Gym", sessions: 1, minutes: 90, priority: 2);
        var stretching = Goal("Stretching", sessions: 7, minutes: 60, priority: 0);

        var plan = Planner.Plan(Request([gym, stretching], oneSlot));

        Assert.Contains(plan.Suggestions, suggestion => suggestion.Goal.Title == "Stretching" && suggestion.Time.Start == Instant.FromUtc(2026, 11, 2, 17, 30));
        Assert.Null(Assert.Single(plan.Shortfalls).Shorter);
    }

    // SCH-5 (review L-1): the gym is open only on Monday, 18:00 to 02:00. Tuesday's first free start (00:00) is in
    // Monday's opening and runs past 02:00; Tuesday's reason is about Tuesday, when it's closed.
    [Fact]
    public void ADayReason_IsAboutThatDaysOwnOpeningHours()
    {
        var mondayLate = new OpeningHours(new Dictionary<IsoDayOfWeek, OpenTimes> { [IsoDayOfWeek.Monday] = new(new LocalTime(18, 0), new LocalTime(2, 0)) });
        var gym = Goal("Gym", sessions: 2, at: Gym, minutes: 150);

        var plan = Planner.Plan(Request([gym]) with
        {
            Context = Context(gymHours: mondayLate),
            Sleep = new SleepWindow(new LocalTime(4, 0), new LocalTime(12, 0), Duration.FromMinutes(30), Duration.FromMinutes(30)),
        });

        Assert.Equal(Instant.FromUtc(2026, 11, 2, 18, 0), Assert.Single(plan.Suggestions).Time.Start);
        var tuesday = Assert.Single(plan.Shortfalls).Days.Single(day => day.Date == new LocalDate(2026, 11, 3));
        Assert.Equal("Town gym is closed that day", tuesday.Reason);
    }

    // P1 (review L-2): another activity whose travel can't be worked out is planned around without its travel, and the
    // plan says so, as the home page does.
    [Fact]
    public void AnActivityWhoseTravelIsUnknown_IsNamedInANote()
    {
        var swim = OneOff("Swim", new LocalDate(2026, 11, 2), new LocalTime(8, 30), 60) with { LocationId = Pool };

        var plan = Planner.Plan(Request([Goal("Stretching", sessions: 1)], [swim]) with { Context = Context(withPoolTravel: false) });

        Assert.Equal(
            "Travel to and from Swim isn't counted, because there's no travel time from Home to Town pool, or from Town pool to Home",
            Assert.Single(plan.Notes));
    }

    // Review L-3: with two sessions of a goal allowed in a day (8 a week), a day with one still gets a reason.
    [Fact]
    public void WithTwoAllowedADay_ADayWithOneSession_StillGetsAReason()
    {
        var oneSlot = new[] { Daily("Day", new LocalTime(7, 30), Duration.FromMinutes(600)), Daily("Evening", new LocalTime(18, 30), Duration.FromMinutes(240)) };

        var plan = Planner.Plan(Request([Goal("Gym", sessions: 8)], oneSlot));

        Assert.Equal(7, plan.Suggestions.Count);
        var shortfall = Assert.Single(plan.Shortfalls);
        Assert.Equal(7, shortfall.Days.Count);
        Assert.Equal("There's no free time; Gym needs 1 h", shortfall.Days[0].Reason);
    }

    // H5, T5 (review L-4): the rest gap is elapsed time (§8). On the night the clocks go forward (28 March 2027), the same
    // clock time is 23 hours later, so it isn't allowed; 19:00 BST (18:00 UTC), 24 hours after Saturday 18:00 GMT, is.
    [Fact]
    public void TheRestGap_IsElapsedTime_OnTheNightTheClocksGoForward()
    {
        var goal = Goal("Intervals", sessions: 1, minutes: 45, demanding: true, days: [IsoDayOfWeek.Sunday], time: TimeOfDay.Evening);
        var ride = OneOff("Hard ride", new LocalDate(2027, 3, 27), new LocalTime(18, 0), 60) with { IsDemanding = true };

        var plan = Planner.Plan(Request([goal], [ride]) with { Week = new DateInterval(new LocalDate(2027, 3, 22), new LocalDate(2027, 3, 28)) });

        Assert.Equal(Instant.FromUtc(2027, 3, 28, 18, 0), Assert.Single(plan.Suggestions).Time.Start);
    }

    // Review L-6: "1 hour", not "1 hours". Free only 17:45-19:00 after a demanding 15-minute ride at 17:30 each day.
    [Fact]
    public void TheRestRule_ReadsWellForOneHour()
    {
        var days = new[]
        {
            Daily("Day", new LocalTime(7, 30), Duration.FromMinutes(600)),
            Daily("Ride", new LocalTime(17, 30), Duration.FromMinutes(15)) with { IsDemanding = true },
            Daily("Evening", new LocalTime(19, 0), Duration.FromMinutes(210)),
        };

        var plan = Planner.Plan(Request([Goal("Intervals", sessions: 1, minutes: 45, demanding: true)], days) with
        {
            Rest = new RestRules(Duration.FromHours(1), 2, new HashSet<IsoDayOfWeek>()),
        });

        Assert.EndsWith("(demanding sessions are at least 1 hour apart)", Assert.Single(plan.Shortfalls).Days[0].Reason);
    }

    // SCH-6, T16: a missed Monday gym session (Mondays 18:00 at Town gym, around office days) gets other times later in
    // the week: the best on each day from now (Wednesday 09:00) on, at most three, on different days, best first, none
    // moving anything else (each passes the check on save against everything as it is).
    [Fact]
    public void AMissedSession_GetsOtherTimesLaterInTheWeek_AndNothingElseMoves()
    {
        var work = WorkWeekdays();
        var gym = new PlannedActivity(Guid.NewGuid(), "Gym",
            new Series(Week.Start, new LocalTime(18, 0), Duration.FromMinutes(50), London, RecurrenceRule.Weekly([IsoDayOfWeek.Monday])), Buffers.None, Gym);
        var now = Instant.FromUtc(2026, 11, 4, 9, 0);
        var session = Goal("Gym", sessions: 1, minutes: 50, at: Gym, time: TimeOfDay.Evening);

        var options = Planner.Options(Request([session], [work, gym]) with { Now = now }, session, most: 3);

        Assert.Equal(3, options.Options.Count);
        Assert.Null(options.None);
        Assert.Equal(3, options.Options.Select(option => option.Time.Start.InZone(London).Date).Distinct().Count());
        Assert.All(options.Options, option =>
        {
            Assert.True(option.Footprint.Whole.Start >= now);
            var check = ActivityCheck.Check(OneOffFor(option), [work, gym], Context());
            Assert.Empty(check.Problems);
            Assert.Empty(check.Clashes);
        });
    }

    // The time it has now isn't offered, but its day still is, at its next best time (code review, Phase 6: the day was
    // dropped). Busy each day until 17:00, so Thursday's best evening time is 18:00, the session's own.
    [Fact]
    public void AnotherTime_SkipsTheCurrentTime_ButKeepsItsDay()
    {
        var busy = Daily("Day", new LocalTime(7, 30), Duration.FromMinutes(570));
        var session = Goal("Gym", sessions: 1, minutes: 50, time: TimeOfDay.Evening, days: [IsoDayOfWeek.Thursday]);
        var current = Instant.FromUtc(2026, 11, 5, 18, 0);

        var options = Planner.Options(Request([session], [busy]), session, most: 3, notAt: current);

        Assert.Equal(Instant.FromUtc(2026, 11, 5, 18, 15), options.Options[0].Time.Start);
        Assert.Equal(Instant.FromUtc(2026, 11, 5, 18, 0), Planner.Options(Request([session], [busy]), session, most: 3).Options[0].Time.Start);
    }

    // ... and when nothing fits in what's left of the week, each day says why.
    [Fact]
    public void AMissedSession_WithNoTimeLeft_SaysWhyEachDay()
    {
        var full = Daily("Busy", new LocalTime(7, 30), Duration.FromMinutes(900));
        var session = Goal("Gym", sessions: 1, minutes: 50);

        var options = Planner.Options(Request([session], [full]) with { Now = Instant.FromUtc(2026, 11, 7, 9, 0) }, session, most: 3);

        Assert.Empty(options.Options);
        Assert.Equal([new LocalDate(2026, 11, 7), new LocalDate(2026, 11, 8)], options.None!.Days.Select(day => day.Date));
    }

    // CAL-7: each day's free time as the calendar is (before any suggestion), not counting sleep or footprints: a day
    // taken up from 07:30 to 17:30 and 18:30 to 22:30 has one free hour; an empty one 15 hours (07:30 to 22:30).
    [Fact]
    public void EachDaysFreeTime_IsTheTotalAndTheLongest_WithoutSleepOrFootprints()
    {
        var busyWeekdays = new[]
        {
            WeekdaysOnly(Daily("Day", new LocalTime(7, 30), Duration.FromMinutes(600))),
            WeekdaysOnly(Daily("Evening", new LocalTime(18, 30), Duration.FromMinutes(240))),
        };

        var plan = Planner.Plan(Request([Goal("Gym", sessions: 3)], busyWeekdays));

        Assert.Equal(7, plan.FreeTime.Count);
        Assert.Equal((Duration.FromHours(1), Duration.FromHours(1)), (plan.FreeTime[0].Total, plan.FreeTime[0].Longest));
        Assert.Equal((Duration.FromHours(15), Duration.FromHours(15)), (plan.FreeTime[5].Total, plan.FreeTime[5].Longest));
    }

    // Only what's left of a day counts: planned on Saturday 7 November at 12:00, Saturday has 10 h 30 min left.
    [Fact]
    public void FreeTime_StartsFromNow()
    {
        var plan = Planner.Plan(Request([]) with { Now = Instant.FromUtc(2026, 11, 7, 12, 0) });

        Assert.Equal([new LocalDate(2026, 11, 7), new LocalDate(2026, 11, 8)], plan.FreeTime.Select(day => day.Date));
        Assert.Equal(Duration.FromMinutes(630), plan.FreeTime[0].Total);
    }

    // CAL-7, §8: the day the clocks go back has 25 hours, and its free time counts the extra one. Week of 19 October
    // 2026, sleep 03:00 to 11:00 (busy 02:30 to 11:30), so the repeated hour isn't asleep. Sunday 25 October is free
    // 23:00 to 02:30 UTC and 11:30 to 24:00 UTC: 16 hours, the longest 12 h 30 min; Saturday 15 hours.
    [Fact]
    public void FreeTime_OnTheDayTheClocksGoBack_CountsItsExtraHour()
    {
        var plan = Planner.Plan(Request([]) with
        {
            Week = new DateInterval(new LocalDate(2026, 10, 19), new LocalDate(2026, 10, 25)),
            Now = Instant.FromUtc(2026, 10, 12, 12, 0),
            Sleep = new SleepWindow(new LocalTime(3, 0), new LocalTime(11, 0), Duration.FromMinutes(30), Duration.FromMinutes(30)),
        });

        Assert.Equal(new LocalDate(2026, 10, 24), plan.FreeTime[5].Date);
        Assert.Equal(Duration.FromHours(15), plan.FreeTime[5].Total);
        Assert.Equal(new LocalDate(2026, 10, 25), plan.FreeTime[6].Date);
        Assert.Equal((Duration.FromHours(16), Duration.FromMinutes(750)), (plan.FreeTime[6].Total, plan.FreeTime[6].Longest));
    }

    private static PlannedActivity WeekdaysOnly(PlannedActivity daily) =>
        daily with
        {
            Series = new Series(daily.Series.FirstDate, daily.Series.StartTime, daily.Series.Length, London,
                RecurrenceRule.Weekly([IsoDayOfWeek.Monday, IsoDayOfWeek.Tuesday, IsoDayOfWeek.Wednesday, IsoDayOfWeek.Thursday, IsoDayOfWeek.Friday])),
        };

    private static string Describe(WeekPlan plan) =>
        string.Join("\n", plan.Suggestions.Select(suggestion => $"{suggestion.Goal.Title} {suggestion.Time}"))
        + "\n" + string.Join("\n", plan.Shortfalls.Select(shortfall => $"{shortfall.Goal.Title} {shortfall.Missing} {string.Join("; ", shortfall.Days.Select(day => $"{day.Date} {day.Reason}"))}"));

    private static PlannedActivity OneOffFor(Suggestion suggestion)
    {
        var start = suggestion.Time.Start.InZone(London).LocalDateTime;
        return new PlannedActivity(Guid.NewGuid(), suggestion.Goal.Title,
            new Series(start.Date, start.TimeOfDay, suggestion.Goal.Length, London, RecurrenceRule.Daily().EndingAfter(1)),
            suggestion.Goal.Buffers, suggestion.Goal.LocationId)
        {
            IsDemanding = suggestion.Goal.IsDemanding,
        };
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
