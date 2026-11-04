using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models.Goals;
using FitnessTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitnessTracker.Web.Controllers;

// GOL-1: the signed-in user's weekly goals, which "Plan my week" finds times for. AppDbContext only ever returns the
// signed-in user's rows, so another user's goal is simply not found (T19, ACC-2).
public class GoalsController(AppDbContext db, PlanningData planning) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var goals = await db.WeeklyGoals.AsNoTracking().ToListAsync();
        ViewData["Names"] = (await MyLocationsAsync()).ToDictionary(location => location.Id, location => location.Name);
        // Highest priority first, as the planner fits them in (§7.4), then by name. Sorted here rather than by SQLite,
        // which puts every capital letter before every lower-case one.
        return View(goals
            .OrderByDescending(goal => goal.Priority)
            .ThenBy(goal => goal.Title, StringComparer.InvariantCultureIgnoreCase)
            .ToList());
    }

    [HttpGet]
    public async Task<IActionResult> Create() => View(await FillListsAsync(new GoalFormViewModel()));

    [HttpPost]
    public async Task<IActionResult> Create(GoalFormViewModel model)
    {
        await FillListsAsync(model);
        await CheckFormAsync(model);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var goal = new WeeklyGoal();
        model.ApplyTo(goal);
        db.WeeklyGoals.Add(goal);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        var goal = await FindAsync(id);
        return goal is null ? NotFound() : View(await FillListsAsync(GoalFormViewModel.From(goal)));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(Guid id, GoalFormViewModel model)
    {
        var goal = await FindAsync(id);
        if (goal is null)
        {
            return NotFound();
        }

        await FillListsAsync(model);
        await CheckFormAsync(model);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // Saved only if nobody has saved this goal since the form was opened (requirements §9).
        db.Entry(goal).Property(saved => saved.Version).OriginalValue = model.Version;
        model.ApplyTo(goal);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            var current = await db.WeeklyGoals.AsNoTracking().SingleOrDefaultAsync(saved => saved.Id == id);
            if (current is null)
            {
                return NotFound();
            }

            ModelState.AddModelError(string.Empty,
                $"This goal was changed in another tab or window after you opened this page. It's now called " +
                $"{current.Title}. Check the details below, then save again to replace them.");
            // The hidden field shows what was posted unless the old value is cleared.
            ModelState.Remove(nameof(model.Version));
            model.Version = current.Version;
            return View(model);
        }

        return RedirectToAction(nameof(Index));
    }

    // Only asks; deleting needs the form post below.
    [HttpGet]
    public async Task<IActionResult> Delete(Guid id)
    {
        var goal = await FindAsync(id);
        return goal is null ? NotFound() : View(goal);
    }

    // version: the one the delete page was opened with. If another tab has changed the goal since, it isn't deleted;
    // the page asks again, naming it as it is now (requirements §9).
    [HttpPost]
    [ActionName(nameof(Delete))]
    public async Task<IActionResult> DeleteConfirmed(Guid id, Guid version)
    {
        var goal = await FindAsync(id);
        if (goal is null)
        {
            return NotFound();
        }

        db.Entry(goal).Property(saved => saved.Version).OriginalValue = version;
        db.WeeklyGoals.Remove(goal);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            var current = await db.WeeklyGoals.AsNoTracking().SingleOrDefaultAsync(saved => saved.Id == id);
            if (current is null)
            {
                return NotFound();
            }

            ModelState.AddModelError(string.Empty,
                $"This goal was changed in another tab or window after you opened this page. It's now called " +
                $"{current.Title}. Delete it anyway?");
            return View(nameof(Delete), current);
        }

        return RedirectToAction(nameof(Index));
    }

    // Through the per-user filter.
    private Task<WeeklyGoal?> FindAsync(Guid id) => db.WeeklyGoals.SingleOrDefaultAsync(goal => goal.Id == id);

    private async Task<GoalFormViewModel> FillListsAsync(GoalFormViewModel model)
    {
        model.Locations = await MyLocationsAsync();
        model.DefaultBuffers = await planning.DefaultBuffersAsync();
        return model;
    }

    private async Task<IReadOnlyList<Location>> MyLocationsAsync() =>
        (await db.Locations.AsNoTracking().ToListAsync())
            .OrderBy(location => location.Name, StringComparer.InvariantCultureIgnoreCase)
            .ToList();

    // Problems with what was typed (listed with each field's own, all at once), and a location that must be the
    // user's own: another user's id is "not found" by the filter, and reported like any choice that isn't on the list.
    private async Task CheckFormAsync(GoalFormViewModel model)
    {
        foreach (var problem in model.ProblemsAcrossTheForm())
        {
            foreach (var field in problem.MemberNames.DefaultIfEmpty(string.Empty))
            {
                ModelState.AddModelError(field, problem.ErrorMessage ?? "");
            }
        }

        if (model.LocationId is { } locationId && !await db.Locations.AnyAsync(location => location.Id == locationId))
        {
            ModelState.AddModelError(nameof(model.LocationId), "Choose a location from the list.");
        }
    }
}
