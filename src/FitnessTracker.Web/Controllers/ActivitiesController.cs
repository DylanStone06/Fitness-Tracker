using System.Globalization;
using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models.Activities;
using FitnessTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace FitnessTracker.Web.Controllers;

// CAL-2, CAL-3: the signed-in user's activities, once or repeating. The per-user filter means another user's
// activity is "not found", exactly like one that doesn't exist (T19), and an activity can only be at the signed-in
// user's own locations. Every save goes through the one check (SCH-1, ActivityChecker).
public class ActivitiesController(AppDbContext db, ActivityChecker checker) : Controller
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
        await CheckFormAsync(model);
        if (!ModelState.IsValid)
        {
            model.Locations = await MyLocationsAsync();
            return View(model);
        }

        // In the user's timezone (§8); changing that preference later moves it, keeping its clock times.
        var preferences = await PreferencesAsync();
        var activity = new Activity { TimeZoneId = preferences.TimeZoneId };
        model.ApplyTo(activity, preferences.WeekStart);
        if (!await PassesTheCheckAsync(activity, model))
        {
            model.Locations = await MyLocationsAsync();
            return View(model);
        }

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

        await CheckFormAsync(model);
        if (!ModelState.IsValid)
        {
            model.Locations = await MyLocationsAsync();
            return View(model);
        }

        // A weekly activity keeps its rule's own week start, so changing the preference since doesn't move a
        // fortnightly activity to the other weeks (scheduling-rules skill). A daily rule has none of its own.
        var weekStart = activity.RepeatRule is { } text && RecurrenceRuleText.Parse(text) is { Frequency: RepeatFrequency.Weekly } rule
            ? rule.WeekStart
            : (await PreferencesAsync()).WeekStart;

        // Saved only if nobody has saved this activity since the form was opened (requirements §9). If the check
        // stops it, the changed activity is simply never saved.
        db.Entry(activity).Property(saved => saved.Version).OriginalValue = model.Version;
        model.ApplyTo(activity, weekStart);
        if (!await PassesTheCheckAsync(activity, model))
        {
            model.Locations = await MyLocationsAsync();
            return View(model);
        }

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

    // SCH-1: the one check, on the activity exactly as it would be saved. Each problem is shown by the field it is
    // about. Clashes (H1) are listed, and need "Save anyway" (§7.2, Q2). True when it can be saved.
    private async Task<bool> PassesTheCheckAsync(Activity activity, ActivityFormViewModel model)
    {
        var result = await checker.CheckAsync(activity);
        foreach (var problem in result.Problems)
        {
            ModelState.AddModelError(FieldFor(problem.Aspect), problem.Message);
        }

        if (result.Problems.Count == 0 && result.Clashes.Count > 0 && !model.ConfirmClashes)
        {
            var zone = DateTimeZoneProviders.Tzdb[activity.TimeZoneId];
            ViewData["Clashes"] = result.Clashes.Select(clash => ActivityCheck.Describe(clash, zone)).ToList();
            ModelState.AddModelError(nameof(model.ConfirmClashes),
                $"It clashes with your other activities, listed below (checked from {result.Checked.Start.ToString("d MMMM uuuu", CultureInfo.InvariantCulture)} " +
                $"for {ActivityCheck.WeeksAhead} weeks). Change it, or tick \"Save anyway\" to keep the clashes.");
        }

        return ModelState.IsValid;
    }

    private static string FieldFor(CheckAspect aspect) => aspect switch
    {
        CheckAspect.Location => nameof(ActivityFormViewModel.LocationId),
        CheckAspect.Time => nameof(ActivityFormViewModel.StartTime),
        CheckAspect.Length => nameof(ActivityFormViewModel.DurationHours),
        _ => nameof(ActivityFormViewModel.EndDate),
    };

    // Problems with what was typed (listed with each field's own, all at once), and a location that must be the
    // user's own: another user's id is "not found" by the filter, and reported like any choice that isn't on the list.
    private async Task CheckFormAsync(ActivityFormViewModel model)
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
