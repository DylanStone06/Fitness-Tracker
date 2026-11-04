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
    private const string ChangedElsewhere =
        "This session was changed in another tab or window after you opened this page. It's shown below as it is now: check it, then make your change again.";

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
        var tooEarly = model.Mark == SessionChoice.Done && !Sessions.CanMarkDone(session.Occurrence, user.Now.ToInstant());

        // FIT-3: a run's boxes are only read when it is saved as done. Anything else ignores them, even what can't be
        // read, as does a session still to come (its page has none) or one of another kind (Q20).
        var savesARun = model.Mark == SessionChoice.Done && !tooEarly && session.Category == ActivityCategory.Run;
        if (!savesARun)
        {
            foreach (var box in SessionLogViewModel.RunBoxes)
            {
                ModelState.Remove(box);
            }
        }

        // Back to planned removes the session's log, notes and all, so what is in the notes box doesn't matter.
        if (model.Mark == SessionChoice.Planned)
        {
            ModelState.Remove(nameof(SessionLogViewModel.Notes));
        }

        // Requirements §9: saved only from a page that showed the session as it is now. The database checks the version
        // again as it saves (a concurrency token), for another tab saving at the same moment.
        var stored = await db.SessionLogs.SingleOrDefaultAsync(log => log.ActivityId == id && log.OccurrenceDate == session.Occurrence.Date);
        if ((stored?.Version ?? Guid.Empty) != model.Version)
        {
            return await ChangedElsewhereAsync(session, user);
        }

        if (tooEarly)
        {
            ModelState.AddModelError(string.Empty, "It hasn't started yet, so it can't be marked done. You can mark it skipped.");
            return View(await PageAsync(session, user));
        }

        if (savesARun)
        {
            foreach (var problem in model.RunProblems(user.Preferences.DistanceUnit))
            {
                foreach (var field in problem.MemberNames.DefaultIfEmpty(string.Empty))
                {
                    ModelState.AddModelError(field, problem.ErrorMessage ?? "");
                }
            }
        }

        // The page again, with what was typed still in its boxes (they show what was posted).
        if (!ModelState.IsValid)
        {
            return View(await PageAsync(session, user));
        }

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
            stored.Notes = model.Notes;
            if (model.Mark == SessionChoice.Skipped)
            {
                // Q28: a skipped session has no details.
                stored.ForgetDetails();
            }
            else if (savesARun)
            {
                model.ApplyRunTo(stored, user.Preferences.DistanceUnit);
            }
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Another tab saved it at the same moment: its version changed (DbUpdateConcurrencyException), or it marked a
            // session that had no mark, which can have only one (AppDbContext).
            db.ChangeTracker.Clear();
            return await ChangedElsewhereAsync(session, user);
        }

        return RedirectToAction(nameof(Log), new { id, date = FormInput.Date.Format(session.Occurrence.Date) });
    }

    // The page as the session is now, saying why nothing was saved. Its hidden version is the current one (not the one
    // posted), so saving from it again works.
    private async Task<IActionResult> ChangedElsewhereAsync(FoundSession session, UserPlanning user)
    {
        ModelState.AddModelError(string.Empty, ChangedElsewhere);
        ModelState.Remove(nameof(SessionLogViewModel.Version));
        return View(nameof(Log), await PageAsync(session, user));
    }

    // The session the activity's rule puts on date, where it is now: null when the activity isn't the user's, isn't
    // exercise (Q20), can't be read (edited by hand), or has no session that day (cancelled, or not in its routine's
    // weeks: CAL-9).
    private async Task<FoundSession?> FindAsync(Guid id, string? date, UserPlanning user)
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

        return ActivityCheck.OccurrenceOn(planned.Series, day) is { } occurrence ? new FoundSession(planned, activity.Category, occurrence) : null;
    }

    private async Task<SessionLogViewModel> PageAsync(FoundSession session, UserPlanning user)
    {
        var log = await db.SessionLogs.AsNoTracking()
            .SingleOrDefaultAsync(log => log.ActivityId == session.Activity.Id && log.OccurrenceDate == session.Occurrence.Date);
        return SessionLogViewModel.For(
            session.Activity, session.Category, session.Occurrence, log, user.Now.ToInstant(), user.Context.Clock, user.Preferences.DistanceUnit);
    }

    // One session: its activity (and that activity's category), and the occurrence.
    private sealed record FoundSession(PlannedActivity Activity, ActivityCategory Category, Occurrence Occurrence);
}
