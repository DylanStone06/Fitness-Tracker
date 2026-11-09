using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Domain.Scheduling;
using NodaTime;

namespace FitnessTracker.Domain.Workouts;

/// <summary>
/// FIT-5: one session of a weekly goal: an occurrence of an activity planned for it
/// (<see cref="PlannedActivity.GoalId"/>), known by the date its rule puts it on (<see cref="Occurrence.Date"/>, for its
/// mark, CAL-4), and the <see cref="Day"/> it starts on in the user's timezone, which says the week it counts in (§8).
/// </summary>
public sealed record GoalSession(Guid Goal, PlannedActivity Activity, Occurrence Occurrence, LocalDate Day);

/// <summary>FIT-5, Q24: which sessions count in a goal's week, as the planner counts them (SCH-3).</summary>
public static class GoalSessions
{
    /// <summary>
    /// The sessions of every goal that start on <paramref name="dates"/> in <paramref name="zone"/>, the user's timezone:
    /// an occurrence belongs to the day it starts (§8), a moved one where it now is (Q22), and a cancelled one isn't
    /// planned at all (Q21). In time order, then by activity and rule date (SCH-7).
    /// </summary>
    public static IReadOnlyList<GoalSession> On(IEnumerable<PlannedActivity> activities, DateInterval dates, DateTimeZone zone)
    {
        var sessions = new List<GoalSession>();
        foreach (var activity in activities)
        {
            if (activity.GoalId is not { } goal)
            {
                continue;
            }

            // Two days either side: in its own timezone, an activity can start on another date than in the user's (up
            // to 26 hours apart). The day in the user's timezone is what decides.
            foreach (var occurrence in activity.Series.OccurrencesBetween(dates.Start.PlusDays(-2), dates.End.PlusDays(2)))
            {
                var day = occurrence.Time.Start.InZone(zone).Date;
                if (dates.Contains(day))
                {
                    sessions.Add(new GoalSession(goal, activity, occurrence, day));
                }
            }
        }

        return sessions
            .OrderBy(session => session.Occurrence.Time.Start)
            .ThenBy(session => session.Activity.Id)
            .ThenBy(session => session.Occurrence.Date)
            .ToList();
    }
}
