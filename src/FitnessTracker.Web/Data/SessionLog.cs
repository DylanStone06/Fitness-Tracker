using FitnessTracker.Domain.Workouts;
using NodaTime;

namespace FitnessTracker.Web.Data;

/// <summary>FIT-3, Q26: the type of run a run's session was, when it was given.</summary>
public enum RunType
{
    Easy,
    Long,
    Tempo,
    Intervals,
    Recovery,

    /// <summary>A type of the user's own, named in <see cref="SessionLog.CustomRunType"/>.</summary>
    Custom,
}

/// <summary>
/// FIT-1: what one session (an occurrence of an exercise activity, Q20) was marked as: done or skipped. A session with
/// none is planned. Known by the date its activity's rule puts it on, as a change to one occurrence is (CAL-4), so a
/// moved session keeps its mark. At most one per session. A run's also says what it was like (FIT-3), each detail
/// optional.
/// </summary>
public class SessionLog : IOwnedByUser, IVersioned
{
    public const int CustomRunTypeMaxLength = 40;

    public const int NotesMaxLength = 2000;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string OwnerId { get; set; } = "";

    public Guid Version { get; set; }

    public Guid ActivityId { get; set; }

    /// <summary>The date the activity's rule puts the session on (where it is now, if moved, is in the activity).</summary>
    public LocalDate OccurrenceDate { get; set; }

    public SessionMark Mark { get; set; }

    /// <summary>FIT-3: how long it took, in seconds (requirements §8), or null when not given.</summary>
    public int? ActualSeconds { get; set; }

    /// <summary>FIT-3: how far, in whole metres (Q25), or null when not given.</summary>
    public int? DistanceMetres { get; set; }

    public RunType? RunType { get; set; }

    /// <summary>The name of a <see cref="Data.RunType.Custom"/> run's type; null for every other.</summary>
    public string? CustomRunType { get; set; }

    /// <summary>FIT-3: how hard it felt, 1 (very easy) to 10 (all out) (Q29), or null when not given.</summary>
    public int? Effort { get; set; }

    /// <summary>FIT-2: notes on any session ("knee a bit sore"); kept when it is marked skipped.</summary>
    public string? Notes { get; set; }

    /// <summary>FIT-2: a gym session's sets, in order (by Position). None for any other kind, or a skipped one.</summary>
    public List<SessionSet> Sets { get; set; } = [];

    /// <summary>Q28: a session marked skipped has no details (a skipped run has no distance).</summary>
    public void ForgetDetails()
    {
        ActualSeconds = null;
        DistanceMetres = null;
        RunType = null;
        CustomRunType = null;
        Effort = null;
    }
}
