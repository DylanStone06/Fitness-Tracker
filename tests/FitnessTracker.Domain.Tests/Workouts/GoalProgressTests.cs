using FitnessTracker.Domain.Workouts;

namespace FitnessTracker.Domain.Tests.Workouts;

// FIT-5, Q24: a weekly goal's week: its sessions (those of the activities linked to it) each done, skipped, not logged
// or still to come, against how many the goal asks for; a skipped one doesn't count towards it (Q38), so the rest are
// "not planned yet". A goal in a category without statuses (meal prep, Q20) has sessions that are just planned.
public class GoalProgressTests
{
    private static readonly SessionState Done = new(SessionStatus.Completed, NotLogged: false);
    private static readonly SessionState Skipped = new(SessionStatus.Skipped, NotLogged: false);
    private static readonly SessionState NotLogged = new(SessionStatus.Planned, NotLogged: true);
    private static readonly SessionState ToCome = new(SessionStatus.Planned, NotLogged: false);
    private static readonly SessionState MovedToCome = new(SessionStatus.Rescheduled, NotLogged: false);
    private static readonly SessionState MovedNotLogged = new(SessionStatus.Rescheduled, NotLogged: true);

    [Fact]
    public void EachSession_IsCountedByItsState()
    {
        var progress = GoalProgress.Of(5, [Done, Skipped, NotLogged, ToCome, MovedToCome]);

        Assert.Equal((5, 1, 1, 1, 2, 0), (progress.Target, progress.Done, progress.Skipped, progress.NotLogged, progress.ToCome, progress.Planned));
    }

    // A moved session (Q22) counts as planned at its new time: not logged once that time has passed.
    [Fact]
    public void AMovedSession_IsNotLogged_OnceItsNewTimeHasPassed()
    {
        Assert.Equal(1, GoalProgress.Of(1, [MovedNotLogged]).NotLogged);
    }

    // Q38: a skipped session doesn't count towards the goal, so it still needs planning; the others do count.
    [Fact]
    public void ASkippedSession_LeavesItsPlaceToBePlanned()
    {
        var progress = GoalProgress.Of(3, [Done, Skipped, ToCome]);

        Assert.Equal(1, progress.NotPlanned);
    }

    // More sessions than the goal asks for: nothing left to plan, never less than none.
    [Fact]
    public void MoreSessionsThanTheTarget_LeaveNothingToPlan()
    {
        Assert.Equal(0, GoalProgress.Of(2, [Done, Done, ToCome]).NotPlanned);
    }

    // Q20: a goal in a category without statuses (meal prep) has sessions that are just planned, and they count.
    [Fact]
    public void SessionsWithoutStatuses_AreJustPlanned_AndCount()
    {
        var progress = GoalProgress.Of(2, [null]);

        Assert.Equal((1, 1), (progress.Planned, progress.NotPlanned));
    }

    [Fact]
    public void NoSessions_LeaveTheWholeTargetToPlan()
    {
        var progress = GoalProgress.Of(3, []);

        Assert.Equal((0, 3), (progress.Done, progress.NotPlanned));
    }
}
