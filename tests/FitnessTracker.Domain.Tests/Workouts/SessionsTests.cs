using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Domain.Workouts;
using NodaTime;

namespace FitnessTracker.Domain.Tests.Workouts;

// FIT-1: a session's status. Planned until it is marked done or skipped (Q21), never completed by itself; a planned
// one whose time has passed shows "Not logged" (T20). A gym session every Tuesday at 18:00 for 50 minutes, from
// Tuesday 3 November 2026 (GMT, so UTC).
public class SessionsTests
{
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly LocalDate Tuesday = new(2026, 11, 3);
    private static readonly Series Gym =
        new(Tuesday, new LocalTime(18, 0), Duration.FromMinutes(50), London, RecurrenceRule.Weekly([IsoDayOfWeek.Tuesday]));

    // T20: once its time has passed, a planned session shows "Not logged", and is still planned.
    [Fact]
    public void T20_APlannedSessionWhoseTimeHasPassed_IsNotLogged_AndStillPlanned()
    {
        var state = Sessions.StateOf(Gym, Tuesday, mark: null, now: Instant.FromUtc(2026, 11, 3, 18, 50));

        Assert.Equal(new SessionState(SessionStatus.Planned, NotLogged: true), state);
    }

    // ... but not while it is still under way.
    [Fact]
    public void ASessionUnderWay_IsPlanned_NotYetNotLogged()
    {
        var state = Sessions.StateOf(Gym, Tuesday, mark: null, now: Instant.FromUtc(2026, 11, 3, 18, 49));

        Assert.Equal(new SessionState(SessionStatus.Planned, NotLogged: false), state);
    }

    [Theory]
    [InlineData(SessionMark.Done, SessionStatus.Completed)]
    [InlineData(SessionMark.Skipped, SessionStatus.Skipped)]
    public void AMarkedSession_HasItsMark_AndIsLogged(SessionMark mark, SessionStatus status)
    {
        var state = Sessions.StateOf(Gym, Tuesday, mark, now: Instant.FromUtc(2026, 11, 10, 12, 0));

        Assert.Equal(new SessionState(status, NotLogged: false), state);
    }

    // Q22: one moved on its own to another time is rescheduled, and not logged once its new time has passed ...
    [Fact]
    public void AMovedSession_IsRescheduled_AndNotLoggedOnceItsNewTimeHasPassed()
    {
        var moved = Gym.MoveOccurrence(Tuesday, new LocalDateTime(2026, 11, 5, 7, 0), Duration.FromMinutes(50));

        Assert.Equal(new SessionState(SessionStatus.Rescheduled, NotLogged: false), Sessions.StateOf(moved, Tuesday, null, Instant.FromUtc(2026, 11, 4, 12, 0)));
        Assert.Equal(new SessionState(SessionStatus.Rescheduled, NotLogged: true), Sessions.StateOf(moved, Tuesday, null, Instant.FromUtc(2026, 11, 5, 8, 0)));
    }

    // ... and done once marked so.
    [Fact]
    public void AMovedSessionMarkedDone_IsCompleted()
    {
        var moved = Gym.MoveOccurrence(Tuesday, new LocalDateTime(2026, 11, 5, 7, 0), Duration.FromMinutes(50));

        Assert.Equal(SessionStatus.Completed, Sessions.StateOf(moved, Tuesday, SessionMark.Done, Instant.FromUtc(2026, 11, 5, 8, 0))?.Status);
    }

    // A cancelled one isn't planned at all (Q21), so it has no state.
    [Fact]
    public void ACancelledSession_HasNoState()
    {
        Assert.Null(Sessions.StateOf(Gym.CancelOccurrence(Tuesday), Tuesday, null, Instant.FromUtc(2026, 11, 10, 12, 0)));
    }

    // Q21: it can be marked done once it has started, not before; skipped at any time.
    [Fact]
    public void ASession_CanBeMarkedDone_OnceItHasStarted()
    {
        var occurrence = ActivityCheck.OccurrenceOn(Gym, Tuesday)!.Value;

        Assert.False(Sessions.CanMarkDone(occurrence, Instant.FromUtc(2026, 11, 3, 17, 59)));
        Assert.True(Sessions.CanMarkDone(occurrence, Instant.FromUtc(2026, 11, 3, 18, 0)));
    }

    // Test review of Phase 7: one that runs past midnight (23:30 to 00:30) has started at 23:30, and isn't "Not logged"
    // until it ends the next day.
    [Fact]
    public void ASessionPastMidnight_StartsOnItsDay_AndIsNotLoggedOnceItEndsTheNextDay()
    {
        var late = new Series(Tuesday, new LocalTime(23, 30), Duration.FromHours(1), London, RecurrenceRule.Weekly([IsoDayOfWeek.Tuesday]));
        var occurrence = ActivityCheck.OccurrenceOn(late, Tuesday)!.Value;

        Assert.False(Sessions.CanMarkDone(occurrence, Instant.FromUtc(2026, 11, 3, 23, 29)));
        Assert.True(Sessions.CanMarkDone(occurrence, Instant.FromUtc(2026, 11, 3, 23, 30)));
        Assert.False(Sessions.StateOf(late, Tuesday, null, Instant.FromUtc(2026, 11, 4, 0, 29))!.NotLogged);
        Assert.True(Sessions.StateOf(late, Tuesday, null, Instant.FromUtc(2026, 11, 4, 0, 30))!.NotLogged);
    }

    // Requirements §8 (test review of Phase 7): on the day the clocks go back, 01:30 happens twice, and the first is the
    // one (01:30 BST, 00:30 UTC). A 30-minute session then has started at 00:30 UTC, and ends at 01:00 UTC.
    [Fact]
    public void OnTheDayTheClocksGoBack_ASessionIsJudgedFromTheFirstOfItsTwoTimes()
    {
        var sunday = new LocalDate(2026, 10, 25);
        var early = new Series(sunday, new LocalTime(1, 30), Duration.FromMinutes(30), London, RecurrenceRule.Weekly([IsoDayOfWeek.Sunday]));
        var occurrence = ActivityCheck.OccurrenceOn(early, sunday)!.Value;

        Assert.False(Sessions.CanMarkDone(occurrence, Instant.FromUtc(2026, 10, 25, 0, 29)));
        Assert.True(Sessions.CanMarkDone(occurrence, Instant.FromUtc(2026, 10, 25, 0, 30)));
        Assert.False(Sessions.StateOf(early, sunday, null, Instant.FromUtc(2026, 10, 25, 0, 59))!.NotLogged);
        Assert.True(Sessions.StateOf(early, sunday, null, Instant.FromUtc(2026, 10, 25, 1, 0))!.NotLogged);
    }

    // §8: on the day they go forward, 01:30 doesn't happen, and the session moves on by the hour skipped, to 02:30 BST
    // (01:30 UTC).
    [Fact]
    public void OnTheDayTheClocksGoForward_ASessionInTheSkippedHourIsJudgedFromAnHourLater()
    {
        var sunday = new LocalDate(2027, 3, 28);
        var early = new Series(sunday, new LocalTime(1, 30), Duration.FromMinutes(30), London, RecurrenceRule.Weekly([IsoDayOfWeek.Sunday]));
        var occurrence = ActivityCheck.OccurrenceOn(early, sunday)!.Value;

        Assert.False(Sessions.CanMarkDone(occurrence, Instant.FromUtc(2027, 3, 28, 1, 29)));
        Assert.True(Sessions.CanMarkDone(occurrence, Instant.FromUtc(2027, 3, 28, 1, 30)));
        Assert.False(Sessions.StateOf(early, sunday, null, Instant.FromUtc(2027, 3, 28, 1, 59))!.NotLogged);
        Assert.True(Sessions.StateOf(early, sunday, null, Instant.FromUtc(2027, 3, 28, 2, 0))!.NotLogged);
    }
}
