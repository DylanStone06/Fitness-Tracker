using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models;
using FitnessTracker.Web.Models.Routines;
using FitnessTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace FitnessTracker.Web.Controllers;

// CAL-9, Q14: routines chosen for ranges of dates (a holiday week), in use on them instead of the usual routine. Listed
// on the Routines page. AppDbContext only ever returns the signed-in user's rows, so another user's dates or routine
// are simply not found (T19, ACC-2).
public class RoutineDatesController(AppDbContext db, ActivityChecker checker) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Create(Guid? routine)
    {
        var model = new RoutineDatesFormViewModel();
        await FillRoutinesAsync(model);
        // From a routine's own button: chosen already, if it's one of the user's.
        model.RoutineId = model.Routines.Any(mine => mine.Id == routine) ? routine : null;
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Create(RoutineDatesFormViewModel model)
    {
        await CheckFormAsync(model, exceptId: null);
        if (!ModelState.IsValid)
        {
            await FillRoutinesAsync(model);
            return View(model);
        }

        var chosen = new RoutineDateRange();
        model.ApplyTo(chosen);
        LocalDate[] changedFrom = [chosen.StartDate];
        var before = await checker.RoutineActivityFindingsAsync(changedFrom);
        db.RoutineDateRanges.Add(chosen);
        await db.SaveChangesAsync();
        await SayWhatBrokeAsync(before);
        return RedirectToAction(nameof(RoutinesController.Index), "Routines");
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        if (await FindAsync(id) is not { } chosen)
        {
            return NotFound();
        }

        var model = RoutineDatesFormViewModel.For(chosen);
        await FillRoutinesAsync(model);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(Guid id, RoutineDatesFormViewModel model)
    {
        if (await FindAsync(id) is not { } chosen)
        {
            return NotFound();
        }

        await CheckFormAsync(model, exceptId: id);
        if (!ModelState.IsValid)
        {
            await FillRoutinesAsync(model);
            return View(model);
        }

        // Both the dates it had and the dates it has now change which routine is in use.
        LocalDate[] changedFrom = [chosen.StartDate, FormInput.ParseDate(model.From)!.Value];
        var before = await checker.RoutineActivityFindingsAsync(changedFrom);
        // Saved only if nobody has saved these dates since the form was opened (requirements §9).
        db.Entry(chosen).Property(saved => saved.Version).OriginalValue = model.Version;
        model.ApplyTo(chosen);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            var current = await db.RoutineDateRanges.AsNoTracking().SingleOrDefaultAsync(saved => saved.Id == id);
            if (current is null)
            {
                return NotFound();
            }

            ModelState.AddModelError(string.Empty,
                "These dates were changed in another tab or window after you opened this page. They're now " +
                $"{RoutineDisplay.Dates(current)}. Check the details below, then save again to replace them.");
            // The hidden field shows what was posted unless the old value is cleared.
            ModelState.Remove(nameof(model.Version));
            model.Version = current.Version;
            await FillRoutinesAsync(model);
            return View(model);
        }

        await SayWhatBrokeAsync(before);
        return RedirectToAction(nameof(RoutinesController.Index), "Routines");
    }

    // Only asks; removing needs the form post below.
    [HttpGet]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (await FindAsync(id) is not { } chosen)
        {
            return NotFound();
        }

        ViewData[RoutineName] = await NameOfAsync(chosen.RoutineId);
        return View(chosen);
    }

    // version: the one the page was opened with. If another tab has changed the dates since, they aren't removed; the
    // page asks again, showing them as they are now (requirements §9).
    [HttpPost]
    [ActionName(nameof(Delete))]
    public async Task<IActionResult> DeleteConfirmed(Guid id, Guid version)
    {
        if (await FindAsync(id) is not { } chosen)
        {
            return NotFound();
        }

        LocalDate[] changedFrom = [chosen.StartDate];
        var before = await checker.RoutineActivityFindingsAsync(changedFrom);
        db.Entry(chosen).Property(saved => saved.Version).OriginalValue = version;
        db.RoutineDateRanges.Remove(chosen);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            var current = await db.RoutineDateRanges.AsNoTracking().SingleOrDefaultAsync(saved => saved.Id == id);
            if (current is null)
            {
                return NotFound();
            }

            ModelState.AddModelError(string.Empty,
                "These dates were changed in another tab or window after you opened this page. Remove them as they are now?");
            ViewData[RoutineName] = await NameOfAsync(current.RoutineId);
            return View(nameof(Delete), current);
        }

        await SayWhatBrokeAsync(before);
        return RedirectToAction(nameof(RoutinesController.Index), "Routines");
    }

    /// <summary>ViewData: the name of the routine the dates are chosen for.</summary>
    public const string RoutineName = "RoutineName";

    // SCH-1, CLAUDE.md rule 3: what the change put on dates where it now breaks a rule or clashes, on the Routines page.
    // Checked from the first day of each range that changed, as before was.
    private async Task SayWhatBrokeAsync(ActivityChecker.RoutineFindings before)
    {
        var broken = await checker.RoutineActivityFindingsSinceAsync(before);
        if (broken.Length > 0)
        {
            TempData[RoutinesController.RoutineChangeFindings] = broken;
        }
    }

    // Through the per-user filter.
    private Task<RoutineDateRange?> FindAsync(Guid id) => db.RoutineDateRanges.SingleOrDefaultAsync(chosen => chosen.Id == id);

    private async Task<string> NameOfAsync(Guid routine) =>
        await db.Routines.Where(mine => mine.Id == routine).Select(mine => mine.Name).SingleOrDefaultAsync() ?? "a routine";

    // The usual routine first, then by name, as the Routines page lists them.
    private async Task FillRoutinesAsync(RoutineDatesFormViewModel model) =>
        model.Routines = (await db.Routines.AsNoTracking().ToListAsync())
            .OrderByDescending(routine => routine.IsUsual)
            .ThenBy(routine => routine.Name, StringComparer.InvariantCultureIgnoreCase)
            .ToList();

    // The form's own problems, then those it needs the database for: the routine must be the user's own (another user's
    // is "not found" by the filter), and two routines can't both be in use on a date, so dates that overlap others are
    // refused, naming the first in the way. (Two tabs saving overlapping dates at the same moment could both be saved;
    // the routine calendar still gives one answer for every date.)
    private async Task CheckFormAsync(RoutineDatesFormViewModel model, Guid? exceptId)
    {
        foreach (var problem in model.Problems())
        {
            foreach (var field in problem.MemberNames.DefaultIfEmpty(string.Empty))
            {
                ModelState.AddModelError(field, problem.ErrorMessage ?? "");
            }
        }

        if (model.RoutineId is { } routine && routine != Guid.Empty && !await db.Routines.AnyAsync(mine => mine.Id == routine))
        {
            ModelState.AddModelError(nameof(model.RoutineId), "Choose a routine from the list.");
        }

        if (FormInput.ParseDate(model.From) is not { } from || FormInput.ParseDate(model.To) is not { } to || to < from)
        {
            return;
        }

        var overlapping = (await db.RoutineDateRanges.AsNoTracking()
                .Where(other => other.Id != exceptId && other.StartDate <= other.EndDate && other.StartDate <= to && other.EndDate >= from)
                .ToListAsync())
            .OrderBy(other => other.StartDate)
            .FirstOrDefault();
        if (overlapping is not null)
        {
            ModelState.AddModelError(nameof(model.From),
                $"These dates overlap those chosen for {await NameOfAsync(overlapping.RoutineId)}, {RoutineDisplay.Dates(overlapping)}. " +
                "Change those first, or choose other dates.");
        }
    }
}
