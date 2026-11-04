namespace FitnessTracker.Domain.Workouts;

/// <summary>
/// FIT-5, Q24: one weekly goal's week: of its sessions (those of the activities linked to it), how many are done,
/// skipped, not logged (T20) and still to come, and how many are just planned (a category without statuses, such as meal
/// prep: Q20), against how many the goal asks for (<see cref="Target"/>).
/// </summary>
public sealed record GoalProgress(int Target, int Done, int Skipped, int NotLogged, int ToCome, int Planned)
{
    /// <summary>
    /// Sessions the goal still needs planning for: its target less every session that counts towards it, which is all
    /// of them but the skipped ones (Q38). Never less than none.
    /// </summary>
    public int NotPlanned => Math.Max(0, Target - (Done + NotLogged + ToCome + Planned));

    /// <summary>
    /// The week of a goal asking for <paramref name="target"/> sessions, from the states of its sessions with statuses
    /// (<see cref="Sessions.StateOf"/>; a moved one, Q22, counts at its new time) and how many it has in a category
    /// without them (<paramref name="withoutStatuses"/>, Q20). No state is what <see cref="Sessions.StateOf"/> gives a
    /// session that isn't planned at all (cancelled, Q21), so it isn't counted (scheduling review of Q38, L5).
    /// </summary>
    public static GoalProgress Of(int target, IEnumerable<SessionState?> sessions, int withoutStatuses = 0)
    {
        int done = 0, skipped = 0, notLogged = 0, toCome = 0;
        foreach (var session in sessions)
        {
            switch (session)
            {
                case null:
                    break;
                case { Status: SessionStatus.Completed }:
                    done++;
                    break;
                case { Status: SessionStatus.Skipped }:
                    skipped++;
                    break;
                case { NotLogged: true }:
                    notLogged++;
                    break;
                default:
                    toCome++;
                    break;
            }
        }

        return new GoalProgress(target, done, skipped, notLogged, toCome, withoutStatuses);
    }
}
