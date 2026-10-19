using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models.Activities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace FitnessTracker.Web.Controllers;

// CAL-2, CAL-3: the signed-in user's activities, once or repeating. The per-user filter means another user's
// activity is "not found", exactly like one that doesn't exist (T19), and an activity can only be at the signed-in
// user's own locations. Checking a new or changed activity against the others (SCH-1) comes next.
public class ActivitiesController(AppDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var activities = await db.Activities.AsNoTracking().ToListAsync();
        ViewData["Names"] = (await MyLocationsAsync()).ToDictionary(location => location.Id, location => location.Name);
        return View(activities
            .OrderBy(activity => activity.StartDate)
            .ThenBy(activity => activity.StartTime)
            .ThenBy(activity => activity.Title, StringComparer.InvariantCultureIgnoreCase)
            .ToList());
    }

    [HttpGet]
    public async Task<IActionResult> Create() => View(new ActivityFormViewModel { Locations = await MyLocationsAsync() });

    [HttpPost]
    public async Task<IActionResult> Create(ActivityFormViewModel model)
    {
        var preferences = await PreferencesAsync();
        await CheckFormAsync(model, preferences.WeekStart);
        if (!ModelState.IsValid)
        {
            model.Locations = await MyLocationsAsync();
            return View(model);
        }

        // In the user's timezone (§8); changing that preference later moves it, keeping its clock times.
        var activity = new Activity { TimeZoneId = preferences.TimeZoneId };
        model.ApplyTo(activity, preferences.WeekStart);
        db.Activities.Add(activity);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        var activity = await FindAsync(id);
        if (activity is null)
        {
            return NotFound();
        }

        var model = ActivityFormViewModel.From(activity);
        model.Locations = await MyLocationsAsync();
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(Guid id, ActivityFormViewModel model)
    {
        var activity = await FindAsync(id);
        if (activity is null)
        {
            return NotFound();
        }

        // A weekly activity keeps its rule's own week start, so changing the preference since doesn't move a
        // fortnightly activity to the other weeks (scheduling-rules skill). A daily rule has none of its own.
        var weekStart = activity.RepeatRule is { } text && RecurrenceRuleText.Parse(text) is { Frequency: RepeatFrequency.Weekly } rule
            ? rule.WeekStart
            : (await PreferencesAsync()).WeekStart;
        await CheckFormAsync(model, weekStart);
        if (!ModelState.IsValid)
        {
            model.Locations = await MyLocationsAsync();
            return View(model);
        }

        // Saved only if nobody has saved this activity since the form was opened (requirements §9).
        db.Entry(activity).Property(saved => saved.Version).OriginalValue = model.Version;
        model.ApplyTo(activity, weekStart);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            var current = await db.Activities.AsNoTracking().SingleOrDefaultAsync(saved => saved.Id == id);
            if (current is null)
            {
                return NotFound();
            }

            ModelState.AddModelError(string.Empty,
                $"This activity was changed in another tab or window after you opened this page. It's now called " +
                $"{current.Title}. Check the details below, then save again to replace them.");
            // The hidden field shows what was posted unless the old value is cleared.
            ModelState.Remove(nameof(model.Version));
            model.Version = current.Version;
            model.Locations = await MyLocationsAsync();
            return View(model);
        }

        return RedirectToAction(nameof(Index));
    }

    // Only asks; deleting needs the form post below.
    [HttpGet]
    public async Task<IActionResult> Delete(Guid id)
    {
        var activity = await FindAsync(id);
        return activity is null ? NotFound() : View(activity);
    }

    // version: the one the delete page was opened with. If another tab has changed the activity since, it isn't
    // deleted; the page asks again, naming it as it is now (requirements §9).
    [HttpPost]
    [ActionName(nameof(Delete))]
    public async Task<IActionResult> DeleteConfirmed(Guid id, Guid version)
    {
        var activity = await FindAsync(id);
        if (activity is null)
        {
            return NotFound();
        }

        db.Entry(activity).Property(saved => saved.Version).OriginalValue = version;
        db.Activities.Remove(activity);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            var current = await db.Activities.AsNoTracking().SingleOrDefaultAsync(saved => saved.Id == id);
            if (current is null)
            {
                return NotFound();
            }

            ModelState.AddModelError(string.Empty,
                $"This activity was changed in another tab or window after you opened this page. It's now called " +
                $"{current.Title}. Delete it anyway?");
            return View(nameof(Delete), current);
        }

        return RedirectToAction(nameof(Index));
    }

    // Through the per-user filter: another user's activity is simply not found.
    private Task<Activity?> FindAsync(Guid id) => db.Activities.SingleOrDefaultAsync(activity => activity.Id == id);

    private async Task<IReadOnlyList<Location>> MyLocationsAsync() =>
        (await db.Locations.AsNoTracking().ToListAsync())
            .OrderBy(location => location.Name, StringComparer.InvariantCultureIgnoreCase)
            .ToList();

    // The user's preferences (timezone and week start for a new activity), or the defaults before they have saved any.
    private async Task<UserPreferences> PreferencesAsync() =>
        await db.UserPreferences.AsNoTracking().SingleOrDefaultAsync() ?? new UserPreferences();

    // Problems across the form (listed with each field's own, all at once), and a location that must be the user's
    // own: another user's id is "not found" by the filter, and reported like any choice that isn't on the list.
    private async Task CheckFormAsync(ActivityFormViewModel model, IsoDayOfWeek weekStart)
    {
        foreach (var problem in model.ProblemsAcrossTheForm(weekStart))
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
