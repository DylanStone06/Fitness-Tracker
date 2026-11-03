using FitnessTracker.Domain.Workouts;

namespace FitnessTracker.Web.Models.Workouts;

/// <summary>FIT-1: how a session's state is written where sessions are listed (the home page, the calendar).</summary>
public static class SessionDisplay
{
    /// <summary>"Planned", "Done", "Skipped", "Rescheduled", or "Not logged" (T20) for one unmarked whose time has passed.</summary>
    public static string Status(SessionState state) => state switch
    {
        { Status: SessionStatus.Completed } => "Done",
        { Status: SessionStatus.Skipped } => "Skipped",
        { NotLogged: true } => "Not logged",
        { Status: SessionStatus.Rescheduled } => "Rescheduled",
        _ => "Planned",
    };
}
