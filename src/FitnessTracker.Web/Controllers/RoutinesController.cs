using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models.Routines;
using FitnessTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitnessTracker.Web.Controllers;

// CAL-9: the signed-in user's routines, kinds of week that repeat over 1 to 4 weeks (A/B weeks). AppDbContext only
// ever returns the signed-in user's rows, so another user's routine is simply not found (T19, ACC-2).
public class RoutinesController(AppDbContext db, PlanningData planning) : Controller
{
    /// <summary>ViewData: today in the user's timezone, for "this week is week B".</summary>
    public const string Today = "Today";

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewData[Today] = (await planning.LoadAsync()).Now.Date;
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
        return View(RoutineFormViewModel.ForNew(user.Now.Date, user.Preferences.WeekStart, firstUsual));
    }

    [HttpPost]
    public async Task<IActionResult> Create(RoutineFormViewModel model)
    {
        await CheckFormAsync(model, exceptId: null);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // Its weeks start as the user's do now, and keep doing so (Routine.WeekStart).
        var weekStart = (await planning.LoadAsync()).Preferences.WeekStart;
        await using var transaction = await db.Database.BeginTransactionAsync();
        await StopOtherUsualRoutinesAsync(model, exceptId: null);
        var routine = new Routine();
        model.ApplyTo(routine, weekStart);
        db.Routines.Add(routine);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        var routine = await FindAsync(id);
        return routine is null ? NotFound() : View(RoutineFormViewModel.From(routine));
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
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        await StopOtherUsualRoutinesAsync(model, exceptId: id);
        // Saved only if nobody has saved this routine since the form was opened (requirements §9). If not, the
        // transaction isn't committed, so the routine that was usual stays usual.
        db.Entry(routine).Property(saved => saved.Version).OriginalValue = model.Version;
        model.ApplyTo(routine, routine.WeekStart);
        try
        {
            await db.SaveChangesAsync();
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
            return View(model);
        }

        await transaction.CommitAsync();
        return RedirectToAction(nameof(Index));
    }

    // Only asks; deleting needs the form post below.
    [HttpGet]
    public async Task<IActionResult> Delete(Guid id)
    {
        var routine = await FindAsync(id);
        return routine is null ? NotFound() : View(routine);
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
            return View(nameof(Delete), current);
        }

        return RedirectToAction(nameof(Index));
    }

    // Through the per-user filter.
    private Task<Routine?> FindAsync(Guid id) => db.Routines.SingleOrDefaultAsync(routine => routine.Id == id);

    // One usual routine at a time: making this one usual makes the other not. Saved first, inside the caller's
    // transaction, so the database's one-usual-per-person rule (AppDbContext) is never broken in between.
    private async Task StopOtherUsualRoutinesAsync(RoutineFormViewModel model, Guid? exceptId)
    {
        if (!model.IsUsual)
        {
            return;
        }

        var others = await db.Routines.Where(routine => routine.IsUsual && routine.Id != exceptId).ToListAsync();
        foreach (var other in others)
        {
            other.IsUsual = false;
        }

        await db.SaveChangesAsync();
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

        if (ModelState.GetFieldValidationState(nameof(model.Name)) == Microsoft.AspNetCore.Mvc.ModelBinding.ModelValidationState.Invalid)
        {
            return;
        }

        var otherNames = await db.Routines.Where(routine => routine.Id != exceptId).Select(routine => routine.Name).ToListAsync();
        if (otherNames.Contains(model.Name, StringComparer.InvariantCultureIgnoreCase))
        {
            ModelState.AddModelError(nameof(model.Name), $"You already have a routine called {model.Name}. Choose a different name.");
        }
    }
}
