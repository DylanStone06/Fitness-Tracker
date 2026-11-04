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

        // Q31: "Add more rows" shows six more, keeping what was typed (the boxes show what was posted), and saves nothing,
        // so nothing typed is checked yet.
        if (model.MoreRows)
        {
            foreach (var entry in ModelState.Values)
            {
                entry.Errors.Clear();
                entry.ValidationState = ModelValidationState.Valid;
            }

            ModelState.Remove(nameof(SessionLogViewModel.Rows));
            var page = await PageAsync(session, user);
            page.ShowRows(model.Rows + SessionLogViewModel.RowsAtATime);
            return View(page);
        }

        // Q21: done once it has started, never before; skipped at any time.
        var tooEarly = model.Mark == SessionChoice.Done && !Sessions.CanMarkDone(session.Occurrence, user.Now.ToInstant());

        // FIT-3, FIT-2: a run's boxes, and a gym session's set rows, are only read when it is saved as done. Anything
        // else ignores them, even what can't be read, as does a session still to come (its page has none) or one of
        // another kind (Q20).
        var savesARun = model.Mark == SessionChoice.Done && !tooEarly && session.Category == ActivityCategory.Run;
        var savesAWorkout = model.Mark == SessionChoice.Done && !tooEarly && session.Category == ActivityCategory.Gym;
        if (!savesARun)
        {
            foreach (var box in SessionLogViewModel.RunBoxes)
            {
                ModelState.Remove(box);
            }
        }

        if (!savesAWorkout)
        {
            foreach (var box in ModelState.Keys.Where(key => key.StartsWith($"{nameof(SessionLogViewModel.Sets)}[", StringComparison.Ordinal)).ToList())
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
        var stored = await db.SessionLogs.Include(log => log.Sets)
            .SingleOrDefaultAsync(log => log.ActivityId == id && log.OccurrenceDate == session.Occurrence.Date);
        if ((stored?.Version ?? Guid.Empty) != model.Version)
        {
            return await ChangedElsewhereAsync(session, user);
        }

        if (tooEarly)
        {
            ModelState.AddModelError(string.Empty, "It hasn't started yet, so it can't be marked done. You can mark it skipped.");
            return View(await PageAsync(session, user));
        }

        IEnumerable<ValidationResult> problems = savesARun ? model.RunProblems(user.Preferences.DistanceUnit)
            : savesAWorkout ? model.WorkoutProblems(user.Preferences.WeightUnit)
            : [];
        foreach (var problem in problems)
        {
            foreach (var field in problem.MemberNames.DefaultIfEmpty(string.Empty))
            {
                ModelState.AddModelError(field, problem.ErrorMessage ?? "");
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
                model.ApplyRunTo(stored, user.Preferences.DistanceUnit);
            }
            else if (savesAWorkout)
            {
                // Added as new rows here: found only through the log, sets with ids of their own would be taken for
                // stored ones.
                db.SessionSets.AddRange(model.ApplyWorkoutTo(stored, user.Preferences.WeightUnit, await ExercisesByNameAsync()));
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
