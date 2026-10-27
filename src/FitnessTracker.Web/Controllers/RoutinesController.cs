using FitnessTracker.Domain.Time;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models;
using FitnessTracker.Web.Models.Routines;
using FitnessTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitnessTracker.Web.Controllers;

// CAL-9: the signed-in user's routines, kinds of week that repeat over 1 to 4 weeks (A/B weeks). AppDbContext only
// ever returns the signed-in user's rows, so another user's routine is simply not found (T19, ACC-2).
public class RoutinesController(AppDbContext db, PlanningData planning, ActivityChecker checker) : Controller
{
    /// <summary>ViewData: today in the user's timezone, for "this week is week B".</summary>
    public const string Today = "Today";

    /// <summary>ViewData: the dates chosen for routines (Q14), earliest first.</summary>
    public const string DatesChosen = "DatesChosen";

    /// <summary>ViewData: the user's locations' names, by id, for each routine's base (TRV-4).</summary>
    public const string LocationNames = "LocationNames";

    /// <summary>
    /// TempData: SCH-1, what a change to which routine is in use when (dates chosen, another made usual, one deleted)
    /// broke, as the check on save sees it, for the list to show.
    /// </summary>
    public const string RoutineChangeFindings = "RoutineChangeFindings";

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewData[Today] = (await planning.LoadAsync()).Now.Date;
        // All of them, those the app can't use too (P1: edited by hand), so they can be seen and removed.
        ViewData[DatesChosen] = (await db.RoutineDateRanges.AsNoTracking().ToListAsync()).OrderBy(chosen => chosen.StartDate).ToList();
        ViewData[LocationNames] = await db.Locations.AsNoTracking().ToDictionaryAsync(location => location.Id, location => location.Name);
        var routines = await db.Routines.AsNoTracking().ToListAsync();
        // The usual routine first, then by name. Sorted here rather than by SQLite, which puts every capital letter
        // before every lower-case one.
        return View(routines
            .OrderByDescending(routine => routine.IsUsual)
            .ThenBy(routine => routine.Name, StringComparer.InvariantCultureIgnoreCase)
            .ToList());
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var user = await planning.LoadAsync();
        var firstUsual = !await db.Routines.AnyAsync(routine => routine.IsUsual);
        return await FormViewAsync(RoutineFormViewModel.ForNew(user.Now.Date, user.Preferences.WeekStart, firstUsual));
    }

    [HttpPost]
    public async Task<IActionResult> Create(RoutineFormViewModel model)
    {
        await CheckFormAsync(model, exceptId: null);
        if (!ModelState.IsValid)
        {
            return await FormViewAsync(model);
        }

        // Its weeks start as the user's do now, and keep doing so (Routine.WeekStart).
        var weekStart = (await planning.LoadAsync()).Preferences.WeekStart;
        // A new routine has no activities or dates yet: only becoming the usual one changes what happens when (or where
        // travel starts from).
        var before = model.IsUsual ? await checker.RoutineActivityFindingsAsync(givesABase: model.BaseLocationId is not null) : null;
        await using var transaction = await db.Database.BeginTransactionAsync();
        var stopped = await StopOtherUsualRoutinesAsync(model, exceptId: null);
        var routine = new Routine();
        model.ApplyTo(routine, weekStart);
        db.Routines.Add(routine);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException) when (model.IsUsual)
        {
            ModelState.AddModelError(nameof(model.IsUsual), UsualAtTheSameTime);
            return await FormViewAsync(model);
        }

        await transaction.CommitAsync();
        await SayWhatStoppedAsync(stopped);
        await SayWhatBrokeAsync(before);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>TempData: activities that no longer happen because their routine stopped being the usual one (P1).</summary>
    public const string UsualChanged = "UsualChanged";

    // Two tabs making different routines usual at once: the database keeps one (AppDbContext), and this one is asked again.
    private const string UsualAtTheSameTime =
        "Another routine was made your usual one at the same moment, in another tab or window. Check your routines, then save again.";

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        var routine = await FindAsync(id);
        return routine is null ? NotFound() : await FormViewAsync(RoutineFormViewModel.From(routine));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(Guid id, RoutineFormViewModel model)
    {
        var routine = await FindAsync(id);
        if (routine is null)
        {
            return NotFound();
        }

        await CheckFormAsync(model, exceptId: id);
        await CheckWeeksStillUsedAsync(model, id);
        if (!ModelState.IsValid)
        {
            return await FormViewAsync(model);
        }

        // Only being usual or not, the cycle and week A change which of its activities' dates happen, and the base where
        // their travel starts (TRV-4); a new name doesn't.
        var changesWhatHappens = model.IsUsual != routine.IsUsual || model.CycleWeeks != routine.CycleWeeks
            || CalendarWeeks.StartOf(FormInput.ParseDate(model.WeekA)!.Value, routine.WeekStart) != routine.WeekAStarts
            || model.BaseLocationId != routine.BaseLocationId;
        // The usual routine (or one becoming or stopping being it) is in use on every date not chosen for another, from
        // today; any other only on its own dates, each range checked from its first day, as when chosen (code review).
        var before = !changesWhatHappens ? null : await checker.RoutineActivityFindingsAsync(
            routine.IsUsual || model.IsUsual ? null : (await DatesChosenForAsync(id)).Select(chosen => chosen.StartDate).ToList(),
            givesABase: model.BaseLocationId is not null);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var stopped = await StopOtherUsualRoutinesAsync(model, exceptId: id);
        if (routine.IsUsual && !model.IsUsual)
        {
            stopped.Add(routine);
        }

        // Saved only if nobody has saved this routine since the form was opened (requirements §9). If not, the
        // transaction isn't committed, so the routine that was usual stays usual.
        db.Entry(routine).Property(saved => saved.Version).OriginalValue = model.Version;
        model.ApplyTo(routine, routine.WeekStart);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException error) when (error is not DbUpdateConcurrencyException && model.IsUsual)
        {
            ModelState.AddModelError(nameof(model.IsUsual), UsualAtTheSameTime);
            return await FormViewAsync(model);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync();
            var current = await db.Routines.AsNoTracking().SingleOrDefaultAsync(saved => saved.Id == id);
            if (current is null)
            {
                return NotFound();
            }

            ModelState.AddModelError(string.Empty,
                $"This routine was changed in another tab or window after you opened this page. It's now called " +
                $"{current.Name}. Check the details below, then save again to replace them.");
            // The hidden field shows what was posted unless the old value is cleared.
            ModelState.Remove(nameof(model.Version));
            model.Version = current.Version;
            return await FormViewAsync(model);
        }

        await transaction.CommitAsync();
        await SayWhatStoppedAsync(stopped);
        await SayWhatBrokeAsync(before);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>TempData: what duplicating a routine made, for the list to say.</summary>
    public const string Duplicated = "Duplicated";

    // CAL-9: a copy of a routine (a holiday week that starts like the usual one). Only its name is asked for.
    [HttpGet]
    public async Task<IActionResult> Duplicate(Guid id)
    {
        if (await FindAsync(id) is not { } original)
        {
            return NotFound();
        }

        return View(new DuplicateRoutineViewModel
        {
            Name = DuplicateRoutineViewModel.NameFor(original),
            Original = original,
            Activities = await db.Activities.CountAsync(activity => activity.RoutineId == id),
        });
    }

    // A new routine with the same weeks, not the usual one and with no dates chosen, so nothing new happens until the
    // user chooses dates or makes it usual; with a copy of each of its activities, in the same week of it, saved
    // together. Activities in every week aren't in a routine, so aren't copied.
    [HttpPost]
    public async Task<IActionResult> Duplicate(Guid id, DuplicateRoutineViewModel model)
    {
        if (await FindAsync(id) is not { } original)
        {
            return NotFound();
        }

        var activities = await db.Activities.AsNoTracking().Where(activity => activity.RoutineId == id).ToListAsync();
        model.Original = original;
        model.Activities = activities.Count;
        await CheckNameIsNewAsync(model.Name, nameof(model.Name), exceptId: null);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var copy = new Routine
        {
            Name = model.Name,
            CycleWeeks = original.CycleWeeks,
            WeekAStarts = original.WeekAStarts,
            WeekStart = original.WeekStart,
            IsUsual = false,
            BaseLocationId = original.BaseLocationId,
        };
        var copies = activities.Select(activity => activity.CopyInto(copy.Id)).ToList();

        // CLAUDE.md rule 3: every save goes through the check. The copy isn't in use on any date yet, so its activities
        // can only have problems their originals already had; any are said, and nothing is saved.
        foreach (var activity in copies)
        {
            foreach (var problem in (await checker.CheckAsync(activity)).Problems)
            {
                ModelState.AddModelError(string.Empty, $"{activity.Title}: {problem.Message}");
            }
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        db.Routines.Add(copy);
        db.Activities.AddRange(copies);
        await db.SaveChangesAsync();
        TempData[Duplicated] = copies.Count switch
        {
            0 => $"{copy.Name} was added. It has no activities yet: choose it for an activity's weeks.",
            1 => $"{copy.Name} was added, with a copy of its activity. It happens once you choose dates for it, or make it your usual routine.",
            var count => $"{copy.Name} was added, with copies of its {count} activities. They happen once you choose dates for it, or make it your usual routine.",
        };
        return RedirectToAction(nameof(Index));
    }

    // Only asks; deleting needs the form post below. A routine with activities can't be deleted: the page says which
    // to change first.
    [HttpGet]
    public async Task<IActionResult> Delete(Guid id)
    {
        var routine = await FindAsync(id);
        if (routine is null)
        {
            return NotFound();
        }

        ViewData[ActivitiesInIt] = await ActivitiesInAsync(id);
        ViewData[DatesChosen] = await DatesChosenForAsync(id);
        return View(routine);
    }

    // version: the one the delete page was opened with. If another tab has changed the routine since, it isn't
    // deleted; the page asks again, naming it as it is now (requirements §9).
    [HttpPost]
    [ActionName(nameof(Delete))]
    public async Task<IActionResult> DeleteConfirmed(Guid id, Guid version)
    {
        var routine = await FindAsync(id);
        if (routine is null)
        {
            return NotFound();
        }

        // Its activities would otherwise never happen, or quietly start happening every week (the database refuses
        // too: AppDbContext).
        var inIt = await ActivitiesInAsync(id);
        if (inIt.Count > 0)
        {
            ViewData[ActivitiesInIt] = inIt;
            ViewData[DatesChosen] = await DatesChosenForAsync(id);
            ModelState.AddModelError(string.Empty, InItMessage(inIt));
            return View(nameof(Delete), routine);
        }

        // The dates chosen for it are deleted with it (AppDbContext) and go back to the usual routine, whose
        // activities then happen on them again. With none, nothing else changes.
        var before = await db.RoutineDateRanges.AnyAsync(chosen => chosen.RoutineId == id) ? await checker.RoutineActivityFindingsAsync() : null;
        db.Entry(routine).Property(saved => saved.Version).OriginalValue = version;
        db.Routines.Remove(routine);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            var current = await db.Routines.AsNoTracking().SingleOrDefaultAsync(saved => saved.Id == id);
            if (current is null)
            {
                return NotFound();
            }

            ModelState.AddModelError(string.Empty,
                $"This routine was changed in another tab or window after you opened this page. It's now called " +
                $"{current.Name}. Delete it anyway?");
            ViewData[ActivitiesInIt] = await ActivitiesInAsync(id);
            ViewData[DatesChosen] = await DatesChosenForAsync(id);
            return View(nameof(Delete), current);
        }

        await SayWhatBrokeAsync(before);
        return RedirectToAction(nameof(Index));
    }

    // Q14: the dates chosen for a routine, earliest first.
    private async Task<List<RoutineDateRange>> DatesChosenForAsync(Guid routineId) =>
        (await db.RoutineDateRanges.AsNoTracking().Where(chosen => chosen.RoutineId == routineId).ToListAsync())
            .OrderBy(chosen => chosen.StartDate)
            .ToList();

    // SCH-1, CLAUDE.md rule 3: what a change to which routine is in use when put on dates where it now breaks a rule or
    // clashes, said on the list (as for a timezone change): its activities, and those in every week whose travel a base
    // moves (ActivityChecker). Nothing to say when nothing was checked (before is null: the change couldn't move anything).
    private async Task SayWhatBrokeAsync(ActivityChecker.RoutineFindings? before)
    {
        if (before is null)
        {
            return;
        }

        var broken = await checker.RoutineActivityFindingsSinceAsync(before);
        if (broken.Length > 0)
        {
            TempData[RoutineChangeFindings] = broken;
        }
    }

    /// <summary>ViewData: the titles of the activities in the routine about to be deleted.</summary>
    public const string ActivitiesInIt = "ActivitiesInIt";

    /// <summary>"1 activity is in this routine: Gym. …", for the delete page.</summary>
    public static string InItMessage(IReadOnlyList<string> titles) =>
        $"{(titles.Count == 1 ? "1 activity is" : $"{titles.Count} activities are")} in this routine: {string.Join(", ", titles)}. " +
        "Change them to another routine, or to every week, before deleting it.";

    // The titles of the activities in a routine, in name order.
    private async Task<List<string>> ActivitiesInAsync(Guid routineId) =>
        (await db.Activities.AsNoTracking().Where(activity => activity.RoutineId == routineId).Select(activity => activity.Title).ToListAsync())
            .Order(StringComparer.InvariantCultureIgnoreCase)
            .ToList();

    // CAL-9: an activity in week B (say) of a routine shortened to one week would never happen again. Named, so the
    // user can change it first or keep the weeks.
    private async Task CheckWeeksStillUsedAsync(RoutineFormViewModel model, Guid id)
    {
        if (!ModelState.IsValid)
        {
            return;
        }

        var stranded = (await db.Activities.AsNoTracking()
                .Where(activity => activity.RoutineId == id && activity.RoutineWeek != null)
                .Select(activity => new { activity.Title, activity.RoutineWeek })
                .ToListAsync())
            .Where(activity => activity.RoutineWeek!.Value - 'A' >= model.CycleWeeks)
            .OrderBy(activity => activity.Title, StringComparer.InvariantCultureIgnoreCase)
            .Select(activity => $"{activity.Title} (week {activity.RoutineWeek})")
            .ToList();
        if (stranded.Count > 0)
        {
            ModelState.AddModelError(nameof(model.CycleWeeks),
                $"With {(model.CycleWeeks == 1 ? "1 week" : $"{model.CycleWeeks} weeks")}, these would never happen: {string.Join(", ", stranded)}. " +
                "Change them to another week first, or keep more weeks.");
        }
    }

    // Through the per-user filter.
    private Task<Routine?> FindAsync(Guid id) => db.Routines.SingleOrDefaultAsync(routine => routine.Id == id);

    // One usual routine at a time: making this one usual makes the other not. Saved first, inside the caller's
    // transaction, so the database's one-usual-per-person rule (AppDbContext) is never broken in between. Returns the
    // routines that stopped being usual.
    private async Task<List<Routine>> StopOtherUsualRoutinesAsync(RoutineFormViewModel model, Guid? exceptId)
    {
        if (!model.IsUsual)
        {
            return [];
        }

        var others = await db.Routines.Where(routine => routine.IsUsual && routine.Id != exceptId).ToListAsync();
        foreach (var other in others)
        {
            other.IsUsual = false;
        }

        await db.SaveChangesAsync();
        return others;
    }

    // P1: a routine that stopped being usual is no longer in use on any date, so its activities stop happening. Said on
    // the list, with how many, rather than letting them disappear from the calendar unexplained.
    private async Task SayWhatStoppedAsync(List<Routine> stopped)
    {
        var notes = new List<string>();
        foreach (var routine in stopped)
        {
            var count = await db.Activities.CountAsync(activity => activity.RoutineId == routine.Id);
            if (count > 0)
            {
                notes.Add($"{routine.Name} is no longer your usual routine, so its {(count == 1 ? "1 activity no longer happens" : $"{count} activities no longer happen")} " +
                    "until it's your usual routine again.");
            }
        }

        if (notes.Count > 0)
        {
            TempData[UsualChanged] = string.Join(" ", notes);
        }
    }

    // Problems across the form (listed with each field's own, all at once), and a name that is already used: two
    // routines with the same name couldn't be told apart when choosing one for an activity. Capitals are ignored, as
    // in the list's order; names only need to differ within one person's routines (the filter).
    private async Task CheckFormAsync(RoutineFormViewModel model, Guid? exceptId)
    {
        foreach (var problem in model.ProblemsAcrossTheForm())
        {
            foreach (var field in problem.MemberNames.DefaultIfEmpty(string.Empty))
            {
                ModelState.AddModelError(field, problem.ErrorMessage ?? "");
            }
        }

        await CheckNameIsNewAsync(model.Name, nameof(model.Name), exceptId);

        // TRV-4: the base must be one of the user's own locations (another user's is "not found" by the filter).
        if (model.BaseLocationId is { } chosen && chosen != Guid.Empty && !await db.Locations.AnyAsync(location => location.Id == chosen))
        {
            ModelState.AddModelError(nameof(model.BaseLocationId), "Choose a base from the list.");
        }
    }

    // The form, with the user's locations to choose a base from, by name. Not home: "Home, as usual" is that already.
    private async Task<ViewResult> FormViewAsync(RoutineFormViewModel model)
    {
        model.Locations = (await db.Locations.AsNoTracking().Where(location => !location.IsHome).ToListAsync())
            .OrderBy(location => location.Name, StringComparer.InvariantCultureIgnoreCase)
            .ToList();
        return View(model);
    }

    // Unless the name already has a problem: refused if another of the user's routines has it, capitals ignored.
    private async Task CheckNameIsNewAsync(string name, string field, Guid? exceptId)
    {
        if (ModelState.GetFieldValidationState(field) == Microsoft.AspNetCore.Mvc.ModelBinding.ModelValidationState.Invalid)
        {
            return;
        }

        var otherNames = await db.Routines.Where(routine => routine.Id != exceptId).Select(routine => routine.Name).ToListAsync();
        if (otherNames.Contains(name, StringComparer.InvariantCultureIgnoreCase))
        {
            ModelState.AddModelError(field, $"You already have a routine called {name}. Choose a different name.");
        }
    }
}
