using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Domain.Time;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models.Preferences;
using FitnessTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace FitnessTracker.Web.Controllers;

// ACC-3, ACC-4, ACC-7: the signed-in user's one set of preferences. There is no id in the address: the per-user
// filter means this only ever reads or changes the signed-in user's own row (T19, PreferencesTests).
public class PreferencesController(AppDbContext db, ActivityChecker checker) : Controller
{
    private const string Saved = "Saved";

    private const string MoveFindings = "MoveFindings";

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewData[Saved] = TempData[Saved];
        ViewData[MoveFindings] = TempData[MoveFindings] as string[] ?? [];
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
        var moved = saved.TimeZoneId == oldTimeZoneId
            ? []
            : await db.Activities.Where(activity => activity.TimeZoneId == oldTimeZoneId).ToListAsync();

        // Q8, CLAUDE.md rule 3: the move goes through the check on save too. What the activities already had before
        // (clashes kept with "Save anyway") is noted first, so only what the move itself breaks is reported. Both checks
        // keep the rest rules just chosen: stricter ones saved at the same time aren't the move's doing.
        var rest = PlanningData.RestRulesOf(saved);
        var before = await FindingsAsync(moved, saved.TimesShownAs(), rest);
        foreach (var activity in moved)
        {
            activity.TimeZoneId = saved.TimeZoneId;
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
        var after = await FindingsAsync(moved, saved.TimesShownAs(), rest);
        TempData[MoveFindings] = after.Except(before).ToArray();
        return RedirectToAction(nameof(Index));
    }

    // Each activity's problems and clashes as the check on save sees them now, in plain English, by title.
    // Both runs write times the way just chosen and keep the rest rules just chosen, so a change of clock or rules
    // alone doesn't make a finding look new.
    private async Task<List<string>> FindingsAsync(IReadOnlyList<Activity> activities, ClockFormat clock, RestRules rest)
    {
        var findings = new List<string>();
        foreach (var activity in activities)
        {
            var result = await checker.CheckAsync(activity, clock, rest);
            var zone = DateTimeZoneProviders.Tzdb[activity.TimeZoneId];
            findings.AddRange(result.Problems.Select(problem => $"{activity.Title}: {problem.Message}"));
            findings.AddRange(result.Clashes.Select(clash => $"{activity.Title}: {ActivityCheck.Describe(clash, zone, clock)}"));
        }

        return findings;
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
