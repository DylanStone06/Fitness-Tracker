using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Domain.Time;
using FitnessTracker.Domain.Workouts;
using FitnessTracker.Web.Data;
using NodaTime;
using NodaTime.Text;

namespace FitnessTracker.Web.Models.Workouts;

/// <summary>FIT-1, Q21: what a session can be marked as on its page: back to planned, done or skipped.</summary>
public enum SessionChoice
{
    Planned,
    Done,
    Skipped,
}

/// <summary>
/// FIT-1: one session's page: when it is, its status (Q22: where a rescheduled one was), "Not logged" once its time has
/// passed unmarked (T20), and the choices for marking it. Only <see cref="Date"/>, <see cref="Version"/> and
/// <see cref="Mark"/> are posted.
/// </summary>
public class SessionLogViewModel
{
    private static readonly LocalDatePattern DayPattern = LocalDatePattern.CreateWithInvariantCulture("ddd d MMM");

    /// <summary>The date the activity's rule puts the session on, which knows it (CAL-4).</summary>
    public string Date { get; set; } = "";

    /// <summary>
    /// Requirements §9: the version of the session's log the page was opened with (empty when it had none), so a save
    /// from a page opened before another tab changed the session is refused.
    /// </summary>
    public Guid Version { get; set; }

    public SessionChoice Mark { get; set; }

    public Guid ActivityId { get; private set; }

    public string Title { get; private set; } = "";

    /// <summary>When it is now: "Thu 29 Oct, 18:00 – 19:00".</summary>
    public string When { get; private set; } = "";

    /// <summary>"Planned", "Done", "Skipped" or "Rescheduled from Tue 27 Oct, 07:00".</summary>
    public string Status { get; private set; } = "";

    public SessionMark? Marked { get; private set; }

    /// <summary>T20: its time has passed and it isn't marked; shown, never a completion.</summary>
    public bool NotLogged { get; private set; }

    /// <summary>Q21: once it has started.</summary>
    public bool CanMarkDone { get; private set; }

    /// <summary>
    /// The page for <paramref name="occurrence"/> of <paramref name="session"/>, with its <paramref name="log"/> (null
    /// when it isn't marked), at <paramref name="now"/>.
    /// </summary>
    public static SessionLogViewModel For(PlannedActivity session, Occurrence occurrence, SessionLog? log, Instant now, ClockFormat clock)
    {
        var series = session.Series;
        var marked = log?.Mark;
        var state = Sessions.StateOf(series, occurrence.Date, marked, now)!;
        var item = new AgendaItem(session, occurrence, null, null, []);
        return new SessionLogViewModel
        {
            Date = FormInput.Date.Format(occurrence.Date),
            Version = log?.Version ?? Guid.Empty,
            ActivityId = session.Id,
            Title = session.Title,
            When = $"{DayPattern.Format(occurrence.Time.Start.InZone(series.Zone).Date)}, {Agenda.Starts(item, clock)} – {Agenda.Ends(item, clock)}",
            Status = state.Status switch
            {
                SessionStatus.Completed => "Done",
                SessionStatus.Skipped => "Skipped",
                SessionStatus.Rescheduled => $"Rescheduled from {WhereItWas(series, occurrence.Date, clock)}",
                _ => "Planned",
            },
            Marked = marked,
            NotLogged = state.NotLogged,
            CanMarkDone = Sessions.CanMarkDone(occurrence, now),
        };
    }

    // Q22: where its rule put it, before it was moved: "Tue 27 Oct, 07:00".
    private static string WhereItWas(Series series, LocalDate date, ClockFormat clock) =>
        $"{DayPattern.Format(date)}, {ClockText.At(date.At(series.StartTime), series.Zone, clock)}";
}
