using FitnessTracker.Domain.Footprints;
using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Domain.Workouts;
using NodaTime;

namespace FitnessTracker.Domain.Tests.Workouts;

// FIT-5, Q24: a goal's sessions in a week are the occurrences of the activities planned for it (GoalId) that start on
// the week's dates in the user's timezone (§8: an occurrence belongs to the day it starts), as the planner counts them.
// The week of 2-8 November 2026, in London (GMT).
public class GoalSessionsTests
{
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly DateTimeZone NewYork = DateTimeZoneProviders.Tzdb["America/New_York"];
    private static readonly DateInterval Week = new(new LocalDate(2026, 11, 2), new LocalDate(2026, 11, 8));
    private static readonly Guid Gym = Guid.NewGuid();
    private static readonly Guid Runs = Guid.NewGuid();

    [Fact]
    public void TheWeeksSessions_AreThoseOfTheActivitiesPlannedForEachGoal()
    {
        var gym = Weekly("Gym", [IsoDayOfWeek.Monday, IsoDayOfWeek.Thursday]) with { GoalId = Gym };
        var run = Weekly("Run", [IsoDayOfWeek.Tuesday]) with { GoalId = Runs };
        var work = Weekly("Work", [IsoDayOfWeek.Monday, IsoDayOfWeek.Tuesday]);

        var sessions = GoalSessions.On([work, run, gym], Week, London);

        Assert.Equal(
            [(Gym, new LocalDate(2026, 11, 2)), (Runs, new LocalDate(2026, 11, 3)), (Gym, new LocalDate(2026, 11, 5))],
            sessions.Select(session => (session.Goal, session.Day)));
    }

    // §8: Sunday 1 November's late session runs into Monday but belongs to Sunday, the week before; Sunday 8
    // November's belongs to this week.
    [Fact]
    public void ASession_BelongsToTheDayItStarts()
    {
        var before = OneOff(new LocalDate(2026, 11, 1), new LocalTime(23, 30), London) with { GoalId = Gym };
        var last = OneOff(new LocalDate(2026, 11, 8), new LocalTime(23, 30), London) with { GoalId = Gym };

        var session = Assert.Single(GoalSessions.On([before, last], Week, London));

        Assert.Equal(new LocalDate(2026, 11, 8), session.Day);
    }

    // CAL-4, Q22: a session moved on its own counts where it now is. Thursday 29 October's, moved to Tuesday 3 November,
    // counts this week (still known by 29 October); Thursday 5 November's, moved to Monday 9 November, doesn't.
    [Fact]
    public void AMovedSession_CountsWhereItNowIs()
    {
        var series = new Series(new LocalDate(2026, 10, 1), new LocalTime(18, 0), Duration.FromMinutes(60), London, RecurrenceRule.Weekly([IsoDayOfWeek.Thursday]))
            .MoveOccurrence(new LocalDate(2026, 10, 29), new LocalDateTime(2026, 11, 3, 18, 0), Duration.FromMinutes(60))
            .MoveOccurrence(new LocalDate(2026, 11, 5), new LocalDateTime(2026, 11, 9, 18, 0), Duration.FromMinutes(60));
        var gym = new PlannedActivity(Guid.NewGuid(), "Gym", series, Buffers.None, null) { GoalId = Gym };

        var session = Assert.Single(GoalSessions.On([gym], Week, London));

        Assert.Equal((new LocalDate(2026, 10, 29), new LocalDate(2026, 11, 3)), (session.Occurrence.Date, session.Day));
    }

    // Q21: a cancelled session isn't planned at all.
    [Fact]
    public void ACancelledSession_IsntOne()
    {
        var gym = Weekly("Gym", [IsoDayOfWeek.Monday]) with { GoalId = Gym };
        gym = gym with { Series = gym.Series.CancelOccurrence(new LocalDate(2026, 11, 2)) };

        Assert.Empty(GoalSessions.On([gym], Week, London));
    }

    // In the user's timezone, as the planner counts: 20:00 in New York on Sunday 1 November is 01:00 on Monday 2
    // November in London, so this week; on Sunday 8 November it is Monday 9 November, the week after.
    [Fact]
    public void TheDay_IsTheOneItStartsInTheUsersTimezone()
    {
        var first = OneOff(new LocalDate(2026, 11, 1), new LocalTime(20, 0), NewYork) with { GoalId = Gym };
        var after = OneOff(new LocalDate(2026, 11, 8), new LocalTime(20, 0), NewYork) with { GoalId = Gym };

        var session = Assert.Single(GoalSessions.On([first, after], Week, London));

        Assert.Equal(new LocalDate(2026, 11, 2), session.Day);
    }

    // FIT-5, SCH-3: progress and Plan my week never disagree about how many sessions a goal has: each goal's number here is
    // the planner's "already planned".
    [Fact]
    public void EachGoalsNumber_IsThePlannersAlreadyPlanned()
    {
        var activities = new[]
        {
            Weekly("Gym", [IsoDayOfWeek.Monday, IsoDayOfWeek.Thursday]) with { GoalId = Gym },
            OneOff(new LocalDate(2026, 11, 1), new LocalTime(23, 30), London) with { GoalId = Gym },
            OneOff(new LocalDate(2026, 11, 8), new LocalTime(23, 30), London) with { GoalId = Runs },
            OneOff(new LocalDate(2026, 11, 1), new LocalTime(20, 0), NewYork) with { GoalId = Runs },
            OneOff(new LocalDate(2026, 11, 8), new LocalTime(20, 0), NewYork) with { GoalId = Runs },
        };
        var goals = new[] { Goal(Gym), Goal(Runs) };
        var request = new PlanRequest(Week, Instant.FromUtc(2026, 10, 26, 12, 0), London, activities, goals,
            new SleepWindow(new LocalTime(23, 0), new LocalTime(7, 0), Duration.Zero, Duration.Zero), RestRules.None,
            new PlanningContext(new LocalDate(2026, 10, 26), null, TravelTimes.None, new Dictionary<Guid, Place>()));

        var planned = Planner.Plan(request).AlreadyPlanned;
        var sessions = GoalSessions.On(activities, Week, London);

        Assert.Equal((2, 2), (planned[Gym], planned[Runs]));
        Assert.Equal((planned[Gym], planned[Runs]), (sessions.Count(session => session.Goal == Gym), sessions.Count(session => session.Goal == Runs)));
    }

    // §8, T4, CAL-4, Q21 (scheduling review of FIT-5, G2): the week of 19-25 October 2026, when London's clocks go back
    // on the Sunday. A daily 01:30 gym session, Wednesday's cancelled, gives 6: Sunday's at the first 01:30 (00:30 UTC).
    // Thursday runs: 15 October's moved to Sunday 25 October at 23:30 GMT counts; 22 October's moved to Monday 26 October
    // at 00:30 doesn't. Every number is still the planner's.
    [Fact]
    public void AcrossTheClocksGoingBack_WithMovesAndACancellation_EachGoalsNumber_IsStillThePlanners()
    {
        var week = new DateInterval(new LocalDate(2026, 10, 19), new LocalDate(2026, 10, 25));
        var gymSeries = new Series(new LocalDate(2026, 10, 12), new LocalTime(1, 30), Duration.FromMinutes(60), London, RecurrenceRule.Daily())
            .CancelOccurrence(new LocalDate(2026, 10, 21));
        var runSeries = new Series(new LocalDate(2026, 10, 1), new LocalTime(18, 0), Duration.FromMinutes(60), London, RecurrenceRule.Weekly([IsoDayOfWeek.Thursday]))
            .MoveOccurrence(new LocalDate(2026, 10, 15), new LocalDateTime(2026, 10, 25, 23, 30), Duration.FromMinutes(60))
            .MoveOccurrence(new LocalDate(2026, 10, 22), new LocalDateTime(2026, 10, 26, 0, 30), Duration.FromMinutes(60));
        var activities = new[]
        {
            new PlannedActivity(Guid.NewGuid(), "Gym", gymSeries, Buffers.None, null) { GoalId = Gym },
            new PlannedActivity(Guid.NewGuid(), "Run", runSeries, Buffers.None, null) { GoalId = Runs },
        };
        var request = new PlanRequest(week, Instant.FromUtc(2026, 10, 12, 12, 0), London, activities, [Goal(Gym), Goal(Runs)],
            new SleepWindow(new LocalTime(23, 0), new LocalTime(7, 0), Duration.Zero, Duration.Zero), RestRules.None,
            new PlanningContext(new LocalDate(2026, 10, 12), null, TravelTimes.None, new Dictionary<Guid, Place>()));

        var planned = Planner.Plan(request).AlreadyPlanned;
        var sessions = GoalSessions.On(activities, week, London);

        Assert.Equal((6, 1), (planned[Gym], planned[Runs]));
        Assert.Equal((planned[Gym], planned[Runs]), (sessions.Count(session => session.Goal == Gym), sessions.Count(session => session.Goal == Runs)));
        Assert.Equal(
            [Instant.FromUtc(2026, 10, 25, 0, 30), Instant.FromUtc(2026, 10, 25, 23, 30)],
            sessions.Where(session => session.Day == new LocalDate(2026, 10, 25)).Select(session => session.Occurrence.Time.Start));
    }

    private static PlannedActivity Weekly(string title, IsoDayOfWeek[] days) =>
        new(Guid.NewGuid(), title, new Series(new LocalDate(2026, 10, 5), new LocalTime(18, 0), Duration.FromMinutes(60), London, RecurrenceRule.Weekly(days)), Buffers.None, null);

    private static PlannedActivity OneOff(LocalDate date, LocalTime start, DateTimeZone zone) =>
        new(Guid.NewGuid(), "Session", new Series(date, start, Duration.FromMinutes(60), zone, RecurrenceRule.Daily().EndingAfter(1)), Buffers.None, null);

    private static PlanGoal Goal(Guid id) =>
        new(id, id == Gym ? "Gym" : "Runs", 10, Duration.FromMinutes(60), Buffers.None, null, false, 1, new HashSet<IsoDayOfWeek>(), TimeOfDay.Any);
}
