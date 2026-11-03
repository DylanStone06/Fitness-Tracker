using FitnessTracker.Domain.Workouts;
using NodaTime;

namespace FitnessTracker.Web.Data;

/// <summary>
/// FIT-1: what one session (an occurrence of an exercise activity, Q20) was marked as: done or skipped. A session with
/// none is planned. Known by the date its activity's rule puts it on, as a change to one occurrence is (CAL-4), so a
/// moved session keeps its mark. At most one per session.
/// </summary>
public class SessionLog : IOwnedByUser, IVersioned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string OwnerId { get; set; } = "";

    public Guid Version { get; set; }

    public Guid ActivityId { get; set; }

    /// <summary>The date the activity's rule puts the session on (where it is now, if moved, is in the activity).</summary>
    public LocalDate OccurrenceDate { get; set; }

    public SessionMark Mark { get; set; }
}
