using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Domain.Scheduling;
using NodaTime;

namespace FitnessTracker.Domain.Workouts;

/// <summary>FIT-1, Q21: what a session has been marked as. Nothing marked is planned.</summary>
public enum SessionMark
{
    Done,
    Skipped,
}

/// <summary>FIT-1: a session's status.</summary>
public enum SessionStatus
{
    /// <summary>Not marked yet.</summary>
    Planned,

    /// <summary>Marked done by the user, never by the app (FIT-1).</summary>
    Completed,

    /// <summary>Marked skipped.</summary>
    Skipped,

    /// <summary>Q22: moved on its own to another time (CAL-4), and not marked yet: planned at its new time.</summary>
    Rescheduled,
}

/// <summary>
/// A session's status, and whether it shows "Not logged" (T20): its time has passed and it isn't marked. That is how
/// it is shown, never a completion: the status stays planned (or rescheduled).
/// </summary>
public sealed record SessionState(SessionStatus Status, bool NotLogged);

/// <summary>FIT-1: the status of a session, one occurrence of an exercise activity (Q20).</summary>
public static class Sessions
{
    /// <summary>
    /// The state of the session <paramref name="series"/>' rule puts on <paramref name="date"/>, where it now is, at
    /// <paramref name="now"/>, with the mark stored for it, if any; null when it is cancelled or the rule doesn't fall
    /// then, since it isn't planned at all (Q21).
    /// </summary>
    public static SessionState? StateOf(Series series, LocalDate date, SessionMark? mark, Instant now)
    {
        if (ActivityCheck.OccurrenceOn(series, date) is not { } occurrence)
        {
            return null;
        }

        var status = mark switch
        {
            SessionMark.Done => SessionStatus.Completed,
            SessionMark.Skipped => SessionStatus.Skipped,
            _ when series.Changes.TryGetValue(date, out var change) && change is OccurrenceChange.Moved => SessionStatus.Rescheduled,
            _ => SessionStatus.Planned,
        };
        return new SessionState(status, NotLogged: mark is null && occurrence.Time.End <= now);
    }

    /// <summary>Q21: a session can be marked done once it has started, never before. It can be skipped at any time.</summary>
    public static bool CanMarkDone(Occurrence occurrence, Instant now) => occurrence.Time.Start <= now;
}
