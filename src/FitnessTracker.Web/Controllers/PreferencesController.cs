using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models.Preferences;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitnessTracker.Web.Controllers;

// ACC-3, ACC-4, ACC-7: the signed-in user's one set of preferences. There is no id in the address: the per-user
// filter means this only ever reads or changes the signed-in user's own row (T19, PreferencesTests).
public class PreferencesController(AppDbContext db) : Controller
{
    private const string Saved = "Saved";

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewData[Saved] = TempData[Saved];
        var saved = await db.UserPreferences.AsNoTracking().SingleOrDefaultAsync();
        return View(PreferencesFormViewModel.From(saved ?? new UserPreferences()));
    }

    [HttpPost]
    public async Task<IActionResult> Index(PreferencesFormViewModel model)
    {
        // Run every time, alongside each field's own checks, so the page lists every problem at once.
        foreach (var problem in model.ProblemsAcrossTheForm())
        {
            foreach (var field in problem.MemberNames.DefaultIfEmpty(string.Empty))
            {
                ModelState.AddModelError(field, problem.ErrorMessage ?? "");
            }
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var saved = await db.UserPreferences.SingleOrDefaultAsync();
        var firstSave = saved is null;
        if (saved is null)
        {
            saved = new UserPreferences();
            db.UserPreferences.Add(saved);
        }
        else if (model.Version == Guid.Empty)
        {
            // The form was opened before another tab saved preferences for the first time.
            return ChangedElsewhere(model, saved.Version);
        }
        else
        {
            // Saved only if nobody has saved since the form was opened (requirements §9).
            db.Entry(saved).Property(preferences => preferences.Version).OriginalValue = model.Version;
        }

        // §8, T6: activities in the old timezone move to the new one, keeping their clock times ("gym at 7am wherever
        // I am"). Saved together with the preferences, so they can't disagree. The page says so before saving.
        var oldTimeZoneId = saved.TimeZoneId;
        model.ApplyTo(saved);
        if (saved.TimeZoneId != oldTimeZoneId)
        {
            foreach (var activity in await db.Activities.Where(activity => activity.TimeZoneId == oldTimeZoneId).ToListAsync())
            {
                activity.TimeZoneId = saved.TimeZoneId;
            }
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException error) when (error is DbUpdateConcurrencyException || firstSave)
        {
            // Another tab saved since this form was opened (a version mismatch), or two tabs saved for the first
            // time at once (refused by the one-row-per-person index). Anything else is a real fault and is rethrown.
            db.ChangeTracker.Clear();
            var current = await db.UserPreferences.AsNoTracking().SingleOrDefaultAsync();
            if (current is null)
            {
                throw;
            }

            return ChangedElsewhere(model, current.Version);
        }

        TempData[Saved] = true;
        return RedirectToAction(nameof(Index));
    }

    // Keep what was typed, give the form the latest version, and ask: saving again is then a deliberate choice.
    private ViewResult ChangedElsewhere(PreferencesFormViewModel model, Guid currentVersion)
    {
        ModelState.AddModelError(string.Empty,
            "Your preferences were changed in another tab or window after you opened this page. " +
            "Check the values below, then save again to replace them.");
        ModelState.Remove(nameof(model.Version));
        model.Version = currentVersion;
        return View(model);
    }
}
