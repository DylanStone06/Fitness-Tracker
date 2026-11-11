using System.ComponentModel.DataAnnotations;
using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Domain.Workouts;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models;
using FitnessTracker.Web.Models.Workouts;
using FitnessTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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

        // Q31: "Add more rows" shows six more, keeping what was typed, and saves nothing, so nothing typed is checked yet.
        if (model.MoreRows)
        {
            foreach (var entry in ModelState.Values)
            {
                Ignore(entry);
            }

            var page = await PageAgainAsync(session, user, model);
            page.ShowRows(model.Rows + SessionLogViewModel.RowsAtATime);
            return View(page);
        }

        // Q27, Q33: what was typed is read in the units the page showed it in (§9: Preferences may have been changed in
        // another tab since it was opened).
        var distanceUnit = model.BoxDistanceUnit ?? user.Preferences.DistanceUnit;
        var weightUnit = model.BoxWeightUnit ?? user.Preferences.WeightUnit;

        // Q21: done once it has started, never before; skipped at any time.
        var tooEarly = model.Mark == SessionChoice.Done && !Sessions.CanMarkDone(session.Occurrence, user.Now.ToInstant());

        // FIT-3, FIT-2: a run's boxes, and a gym session's set rows, are only read when it is saved as done. Anything
        // else ignores them, even what can't be read, as does a session still to come (its page has none) or one of
        // another kind (Q20). Back to planned removes the session's log, notes and all, so it ignores the notes box too.
        var savesARun = model.Mark == SessionChoice.Done && !tooEarly && session.Category == ActivityCategory.Run;
        var savesAWorkout = model.Mark == SessionChoice.Done && !tooEarly && session.Category == ActivityCategory.Gym;
        var savesAnOther = model.Mark == SessionChoice.Done && !tooEarly && session.Category == ActivityCategory.OtherExercise;
        var ignored = (savesARun ? [] : SessionLogViewModel.RunTypeBoxes)
            .Concat(savesAnOther ? [] : SessionLogViewModel.OtherTypeBoxes)
            .Concat(savesARun || savesAnOther ? [] : SessionLogViewModel.DetailBoxes)
            .Concat(model.Mark == SessionChoice.Planned ? [nameof(SessionLogViewModel.Notes)] : [])
            .ToHashSet(StringComparer.Ordinal);
        foreach (var (box, entry) in ModelState)
        {
            if (entry is not null &&
                (ignored.Contains(box) || (!savesAWorkout && box.StartsWith($"{nameof(SessionLogViewModel.Sets)}[", StringComparison.Ordinal))))
            {
                Ignore(entry);
            }
        }

        // Requirements §9: saved only from a page that showed the session as it is now. The database checks the version
        // again as it saves (a concurrency token), for another tab saving at the same moment.
        var stored = await db.SessionLogs.Include(log => log.Sets)
            .SingleOrDefaultAsync(log => log.ActivityId == id && log.OccurrenceDate == session.Occurrence.Date);
        if ((stored?.Version ?? Guid.Empty) != model.Version)
        {
            return await ChangedElsewhereAsync(session, user, model);
        }

        if (tooEarly)
        {
            ModelState.AddModelError(string.Empty, "It hasn't started yet, so it can't be marked done. You can mark it skipped.");
            return View(await PageAgainAsync(session, user, model));
        }

        IEnumerable<ValidationResult> problems = savesARun ? model.RunProblems(distanceUnit)
            : savesAnOther ? model.OtherProblems(distanceUnit)
            : savesAWorkout ? model.WorkoutProblems(weightUnit)
            : [];
        foreach (var problem in problems)
        {
            foreach (var field in problem.MemberNames.DefaultIfEmpty(string.Empty))
            {
                ModelState.AddModelError(field, problem.ErrorMessage ?? "");
            }
        }

        if (!ModelState.IsValid)
        {
            return View(await PageAgainAsync(session, user, model));
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
            else
            {
                // §9: every save gives the log a new version, even one that only changes its sets (rows of their own),
                // so a page opened before it can't replace them.
                db.Entry(stored).State = EntityState.Modified;
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
                model.ApplyRunTo(stored, distanceUnit);
            }
            else if (savesAnOther)
            {
                model.ApplyOtherTo(stored, distanceUnit);
            }
            else if (savesAWorkout)
            {
                // Added as new rows here: found only through the log, sets with ids of their own would be taken for
                // stored ones.
                db.SessionSets.AddRange(model.ApplyWorkoutTo(stored, weightUnit, await ExercisesByNameAsync()));
            }
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Another tab saved it at the same moment: its version changed (DbUpdateConcurrencyException), or it marked a
            // session that had no mark, which can have only one (AppDbContext). Either way its version is no longer the
            // one this page showed. If it still is, nothing else changed it, so this is a real fault, for the error page:
            // saying another tab changed it would only have the user try again, and again.
            db.ChangeTracker.Clear();
            var versionNow = await db.SessionLogs.Where(log => log.ActivityId == id && log.OccurrenceDate == session.Occurrence.Date)
                .Select(log => (Guid?)log.Version)
                .SingleOrDefaultAsync();
            if ((versionNow ?? Guid.Empty) == model.Version)
            {
                throw;
            }

            return await ChangedElsewhereAsync(session, user, model);
        }

        return RedirectToAction(nameof(Log), new { id, date = FormInput.Date.Format(session.Occurrence.Date) });
    }

    // Requirements §9: the page again, saying what the session is now, with what was typed still in its boxes, as other
    // pages say it when what they show was changed in another tab. Its hidden version is the current one (not the one
    // posted), so saving from it again works.
    private async Task<IActionResult> ChangedElsewhereAsync(FoundSession session, UserPlanning user, SessionLogViewModel posted)
    {
        var page = await PageAgainAsync(session, user, posted);
        // The page's own notes are the ones saved now (its box shows what was typed).
        ModelState.AddModelError(string.Empty, ChangedElsewhere(page.Marked, page.Notes));
        ModelState.Remove(nameof(SessionLogViewModel.Version));
        return View(nameof(Log), page);
    }

    // What a session changed in another tab is now: its mark and notes. Its details and sets are shown on the page, as
    // saved, above the boxes. With no mark it has no log, so it was marked when the page was opened (its version said so).
    private static string ChangedElsewhere(SessionMark? mark, string? notes)
    {
        var notesNow = notes is null ? "with no notes" : $"with the notes \"{notes}\"";
        var now = mark switch
        {
            SessionMark.Done => $"marked done, {notesNow}",
            SessionMark.Skipped => $"marked skipped, {notesNow}",
            _ => "back to planned",
        };
        return $"This session was changed in another tab or window after you opened this page. It's now {now}. " +
            "What you typed is still below: check it, then make your change again.";
    }

    // The page again after a problem, with what was typed still in its boxes (SessionLogViewModel.KeepTyped).
    private async Task<SessionLogViewModel> PageAgainAsync(FoundSession session, UserPlanning user, SessionLogViewModel posted)
    {
        var page = await PageAsync(session, user);
        page.KeepTyped(posted);
        // The hidden field shows how many rows the page has now, not how many were posted.
        ModelState.Remove(nameof(SessionLogViewModel.Rows));
        return page;
    }

    // A box this save doesn't read: nothing in it stops the save, even what can't be read, but it keeps what was typed
    // in it (a box shows what was posted in it over the page's own value), so a page shown again after a problem has it.
    private static void Ignore(ModelStateEntry entry)
    {
        entry.Errors.Clear();
        entry.ValidationState = ModelValidationState.Valid;
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
        var log = await db.SessionLogs.AsNoTracking().Include(log => log.Sets)
            .SingleOrDefaultAsync(log => log.ActivityId == session.Activity.Id && log.OccurrenceDate == session.Occurrence.Date);
        var model = SessionLogViewModel.For(
            session.Activity, session.Category, session.Occurrence, log, user.Now.ToInstant(), user.Context.Clock, user.Preferences.DistanceUnit);
        if (session.Category == ActivityCategory.Gym)
        {
            // FIT-2: the workout as saved (a done session's), and Q30's suggestions: the user's own exercises first, then
            // the starter list's others. The per-user filter means another user's exercises are never among them (T19).
            var exercises = await db.Exercises.AsNoTracking().OrderBy(exercise => exercise.Name).ToListAsync();
            var names = exercises.ToDictionary(exercise => exercise.Id, exercise => exercise.Name);
            var saved = log is { Mark: SessionMark.Done }
                ? log.Sets.OrderBy(set => set.Position)
                    .Select(set => new WorkoutSet(names.GetValueOrDefault(set.ExerciseId, "?"), set.Reps, set.WeightGrams, set.RestSeconds))
                    .ToList()
                : [];
            var ownKeys = exercises.Select(exercise => exercise.NameKey).ToHashSet(StringComparer.Ordinal);
            var suggestions = exercises.Select(exercise => exercise.Name)
                .Concat(StarterExercises.Names.Where(name => !ownKeys.Contains(ExerciseNames.Key(name))))
                .ToList();
            model.FillWorkout(saved, user.Preferences.WeightUnit, suggestions);
        }

        return model;
    }

    // Q30: finds the user's exercise by name, ignoring capitals, or adds it the first time; one lookup per save.
    private async Task<Func<string, Exercise>> ExercisesByNameAsync()
    {
        var known = await db.Exercises.ToDictionaryAsync(exercise => exercise.NameKey, StringComparer.Ordinal);
        return name =>
        {
            var key = ExerciseNames.Key(name);
            if (!known.TryGetValue(key, out var exercise))
            {
                exercise = new Exercise { Name = ExerciseNames.Tidy(name), NameKey = key };
                db.Exercises.Add(exercise);
                known[key] = exercise;
            }

            return exercise;
        };
    }

    // One session: its activity (and that activity's category), and the occurrence.
    private sealed record FoundSession(PlannedActivity Activity, ActivityCategory Category, Occurrence Occurrence);
}
