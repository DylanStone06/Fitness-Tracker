using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Domain.Workouts;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models;
using FitnessTracker.Web.Models.Workouts;
using FitnessTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.TimeZones;

namespace FitnessTracker.Web.Controllers;

// FIT-1, Q20-Q22: one session's page. A session is an occurrence of an exercise activity, known by the activity's id
// and the date its rule puts it on (as for CAL-4). It shows its status, and marks it done (once it has started), skipped,
// or back to planned. The app never marks one itself (FIT-1, T20). AppDbContext only ever returns the signed-in user's
// rows, so another user's activity is simply not found (T19, ACC-2).
public class SessionsController(AppDbContext db, PlanningData planning) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Log(Guid id, string? date)
    {
        var user = await planning.LoadAsync();
        if (await FindAsync(id, date, user) is not { } session)
        {
            return NotFound();
        }

        return View(await PageAsync(session, user));
    }

    [HttpPost]
    public async Task<IActionResult> Log(Guid id, SessionLogViewModel model)
    {
        var user = await planning.LoadAsync();
        if (await FindAsync(id, model.Date, user) is not { } session)
        {
            return NotFound();
        }

        // Q21: done once it has started, never before; skipped at any time.
        if (model.Mark == SessionChoice.Done && !Sessions.CanMarkDone(session.Occurrence, user.Now.ToInstant()))
        {
            ModelState.AddModelError(string.Empty, "It hasn't started yet, so it can't be marked done. You can mark it skipped.");
            return View(await PageAsync(session, user));
        }

        var stored = await db.SessionLogs.SingleOrDefaultAsync(log => log.ActivityId == id && log.OccurrenceDate == session.Occurrence.Date);
        if (model.Mark == SessionChoice.Planned)
        {
            if (stored is not null)
            {
                db.SessionLogs.Remove(stored);
            }
        }
        else
        {
            if (stored is null)
            {
                stored = new SessionLog { ActivityId = id, OccurrenceDate = session.Occurrence.Date };
                db.SessionLogs.Add(stored);
            }

            stored.Mark = model.Mark == SessionChoice.Done ? SessionMark.Done : SessionMark.Skipped;
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Two tabs marking it at the same moment: one mark per session (AppDbContext), and this one is asked again.
            db.ChangeTracker.Clear();
            ModelState.AddModelError(string.Empty, "This session was marked in another tab or window at the same moment. Check it below, then mark it again.");
            return View(await PageAsync(session, user));
        }

        return RedirectToAction(nameof(Log), new { id, date = FormInput.Date.Format(session.Occurrence.Date) });
    }

    // The session the activity's rule puts on date, where it is now: null when the activity isn't the user's, isn't
    // exercise (Q20), can't be read (edited by hand), or has no session that day (cancelled, or not in its routine's
    // weeks: CAL-9).
    private async Task<(PlannedActivity Activity, Occurrence Occurrence)?> FindAsync(Guid id, string? date, UserPlanning user)
    {
        var activity = await db.Activities.AsNoTracking().SingleOrDefaultAsync(activity => activity.Id == id);
        if (activity is null || !activity.Category.IsExercise() || FormInput.ParseDate(date) is not { } day)
        {
            return null;
        }

        PlannedActivity planned;
        try
        {
            planned = activity.ToPlannedActivity(user.Routines.Calendar);
        }
        catch (Exception error) when (error is FormatException or DateTimeZoneNotFoundException)
        {
            return null;
        }

        return ActivityCheck.OccurrenceOn(planned.Series, day) is { } occurrence ? (planned, occurrence) : null;
    }

    private async Task<SessionLogViewModel> PageAsync((PlannedActivity Activity, Occurrence Occurrence) session, UserPlanning user)
    {
        var marked = await db.SessionLogs.AsNoTracking()
            .Where(log => log.ActivityId == session.Activity.Id && log.OccurrenceDate == session.Occurrence.Date)
            .Select(log => (SessionMark?)log.Mark)
            .SingleOrDefaultAsync();
        return SessionLogViewModel.For(session.Activity, session.Occurrence, marked, user.Now.ToInstant(), user.Context.Clock);
    }
}
