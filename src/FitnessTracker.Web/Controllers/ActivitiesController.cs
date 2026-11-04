using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models;
using FitnessTracker.Web.Models.Activities;
using FitnessTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.TimeZones;

namespace FitnessTracker.Web.Controllers;

// CAL-2, CAL-3: the signed-in user's activities, once or repeating. The per-user filter means another user's
// activity is "not found", exactly like one that doesn't exist (T19), and an activity can only be at the signed-in
// user's own locations. Every save goes through the one check (SCH-1, ActivityChecker).
public class ActivitiesController(AppDbContext db, ActivityChecker checker, PlanningData planning, WeekPlanning weekPlanning) : Controller
{
    private const string CheckNotes = "CheckNotes";

    /// <summary>TempData: what an edit of the whole activity did to its one-off changes, shown on the list or calendar.</summary>
    public const string SeriesEdit = "SeriesEdit";

    /// <summary>ViewData: how the list and the delete page write times (ACC-3).</summary>
    public const string Clock = "Clock";

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewData[CheckNotes] = TempData[CheckNotes] as string[] ?? [];
        ViewData[SeriesEdit] = TempData[SeriesEdit] as string;
        ViewData[Clock] = (await PreferencesAsync()).TimesShownAs();
        var activities = await db.Activities.AsNoTracking().ToListAsync();
        ViewData["Names"] = (await MyLocationsAsync()).ToDictionary(location => location.Id, location => location.Name);
        return View(activities
            .OrderBy(activity => activity.StartDate)
            .ThenBy(activity => activity.StartTime)
            .ThenBy(activity => activity.Title, StringComparer.InvariantCultureIgnoreCase)
            .ToList());
    }

    // From the calendar: the date and time selected there, and back to the calendar once saved. From "Plan my week"
    // (SCH-4, "Change"): a session of the user's own weekly goal at the suggested time, back to the plan once saved; a
    // goal that isn't theirs is simply not found, and the form starts empty.
    [HttpGet]
    public async Task<IActionResult> Create(
        string? date, string? time, bool returnToCalendar = false, Guid? goal = null, bool everyWeek = false, bool returnToPlan = false)
    {
        var forGoal = goal is { } goalId ? await db.WeeklyGoals.AsNoTracking().SingleOrDefaultAsync(saved => saved.Id == goalId) : null;
        var model = forGoal is null
            ? ActivityFormViewModel.ForNew(date, time, returnToCalendar)
            : ActivityFormViewModel.ForGoal(forGoal, date, time, everyWeek, returnToPlan);
        return View(await FillListsAsync(model));
    }

    [HttpPost]
    public async Task<IActionResult> Create(ActivityFormViewModel model)
    {
        await FillListsAsync(model);
        await CheckFormAsync(model);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // In the user's timezone (§8); changing that preference later moves it, keeping its clock times.
        var preferences = await PreferencesAsync();
        var activity = new Activity { TimeZoneId = preferences.TimeZoneId };
        model.ApplyTo(activity, preferences.WeekStart);
        if (!await PassesTheCheckAsync(activity, model, await checker.CheckAsync(activity)))
        {
            return View(model);
        }

        db.Activities.Add(activity);
        await db.SaveChangesAsync();
        return AfterSaving(model);
    }

    // From the calendar, a drag fills in the new start and length (CAL-8): saved only once checked and confirmed here.
    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, bool returnToCalendar = false, string? start = null, int? minutes = null)
    {
        var activity = await FindAsync(id);
        if (activity is null)
        {
            return NotFound();
        }

        var model = ActivityFormViewModel.From(activity);
        model.ReturnToCalendar = returnToCalendar;
        model.FillDragged(start, minutes);
        await FillListsAsync(model);
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

        await FillListsAsync(model);
        await CheckFormAsync(model);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // A weekly activity keeps its rule's own week start, so changing the preference since doesn't move a
        // fortnightly activity to the other weeks (scheduling-rules skill). A daily rule has none of its own.
        var weekStart = activity.RepeatRule is { } text && RecurrenceRuleText.Parse(text) is { Frequency: RepeatFrequency.Weekly } rule
            ? rule.WeekStart
            : (await PreferencesAsync()).WeekStart;

        // CAL-4, T8: occurrences changed on their own keep their changes where the edited activity still falls on
        // their dates.
        var before = SeriesOf(activity);

        // Saved only if nobody has saved this activity since the form was opened (requirements §9). If the check
        // stops it, the changed activity is simply never saved.
        db.Entry(activity).Property(saved => saved.Version).OriginalValue = model.Version;
        model.ApplyTo(activity, weekStart);
        // An activity that couldn't be read as it was (edited by hand: a length of 0, say) keeps changes that can be
        // read with it as it is now. Others can't be kept.
        before ??= activity.OccurrenceChanges is null ? null : SeriesOf(activity);
        var unreadableChanges = before is null && activity.OccurrenceChanges is not null;
        var (kept, removed) = before is null ? (0, 0) : activity.KeepChangesFrom(before);
        if (unreadableChanges)
        {
            activity.OccurrenceChanges = null;
        }

        if (!await PassesTheCheckAsync(activity, model, await checker.CheckAsync(activity)))
        {
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
            return View(model);
        }

        if (SeriesEditMessage(kept, removed, unreadableChanges) is { } message)
        {
            TempData[SeriesEdit] = message;
        }

        return AfterSaving(model);
    }

    // CAL-4: what happened to the occurrences changed on their own, in plain English (P5: never silently).
    private static string? SeriesEditMessage(int kept, int removed, bool unreadable)
    {
        var parts = new List<string>();
        if (kept > 0)
        {
            parts.Add(kept == 1
                ? "1 occurrence you changed on its own keeps its change"
                : $"{kept} occurrences you changed on their own keep their changes");
        }

        if (removed > 0)
        {
            parts.Add(removed == 1
                ? "1 change was on a day it no longer happens, so it was removed"
                : $"{removed} changes were on days it no longer happens, so they were removed");
        }

        if (unreadable)
        {
            parts.Add("the activity as it was couldn't be read, so its changes to single occurrences were removed");
        }

        return parts.Count == 0 ? null : $"Saved. {string.Join("; ", parts)}.";
    }

    // The activity's series with its one-off changes, or null when it can't be read (edited by hand).
    private static Series? SeriesOf(Activity activity)
    {
        try
        {
            return activity.ToPlannedActivity().Series;
        }
        catch (Exception error) when (error is FormatException or DateTimeZoneNotFoundException)
        {
            return null;
        }
    }

    // Back where the form was opened from: the calendar or the plan, on the activity's week, or the list.
    private RedirectToActionResult AfterSaving(ActivityFormViewModel model) =>
        model.ReturnToCalendar ? RedirectToAction(nameof(CalendarController.Index), "Calendar", new { date = model.SavedStartDate })
        : model.ReturnToPlan ? RedirectToAction(nameof(PlanController.Index), "Plan", new { date = model.SavedStartDate })
        : RedirectToAction(nameof(Index));

    // CAL-4: one occurrence of a repeating activity, known by the date its rule puts it on. A one-off has only one, so
    // it goes to the ordinary form. From the calendar a drag fills in the new time (CAL-8: the form does the same).
    [HttpGet]
    public async Task<IActionResult> Occurrence(Guid id, string? date, string? start, int? minutes, bool returnToCalendar = false)
    {
        var activity = await FindAsync(id);
        if (activity is null)
        {
            return NotFound();
        }

        if (activity.RepeatRule is null)
        {
            return RedirectToAction(nameof(Edit), new { id, returnToCalendar = returnToCalendar ? "true" : null });
        }

        if (OccurrenceOf(activity, date) is not { } found)
        {
            return NotFound();
        }

        var model = OccurrenceFormViewModel.For(activity, found.Series, found.Date, returnToCalendar, (await PreferencesAsync()).TimesShownAs());
        model.FillDragged(start, minutes);
        return View(model);
    }

    // Move, cancel or put back one occurrence, leaving the others alone (P5). Through the check on save (SCH-1), which
    // reports only what this change breaks: problems and clashes the activity already had stay as they were.
    [HttpPost]
    public async Task<IActionResult> Occurrence(Guid id, OccurrenceFormViewModel model)
    {
        var activity = await FindAsync(id);
        if (activity is null)
        {
            return NotFound();
        }

        if (activity.RepeatRule is null)
        {
            return RedirectToAction(nameof(Edit), new { id, returnToCalendar = model.ReturnToCalendar ? "true" : null });
        }

        if (OccurrenceOf(activity, model.Date) is not { } found)
        {
            return NotFound();
        }

        model.Describe(activity, found.Series, found.Date, (await PreferencesAsync()).TimesShownAs());
        if (model.Change != OccurrenceChangeChoice.Move)
        {
            foreach (var box in OccurrenceFormViewModel.MoveBoxes)
            {
                ModelState.Remove(box);
            }
        }

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

        var changed = model.Change switch
        {
            OccurrenceChangeChoice.Cancel => found.Series.CancelOccurrence(found.Date),
            OccurrenceChangeChoice.Restore => found.Series.RestoreOccurrence(found.Date),
            _ => found.Series.MoveOccurrence(found.Date, model.NewStart, model.NewLength),
        };

        // Saved only if nobody has saved this activity since the page was opened (requirements §9).
        db.Entry(activity).Property(saved => saved.Version).OriginalValue = model.Version;
        // SCH-1 on that occurrence, where it now is (nothing for a cancelled one): what the rest of the series already
        // had, accepted clashes included, isn't this change's doing.
        activity.OccurrenceChanges = OccurrenceChangesText.Format(changed.Changes);
        if (!await PassesTheCheckAsync(activity, model, await checker.CheckOccurrenceAsync(activity, found.Date), OccurrenceFieldFor, oneOccurrence: true))
        {
            return View(model);
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await db.Activities.AsNoTracking().AnyAsync(saved => saved.Id == id))
            {
                return NotFound();
            }

            ModelState.AddModelError(string.Empty,
                "This activity was changed in another tab or window after you opened this page, so nothing was saved. " +
                "Open the occurrence again from the calendar or the list to see it as it is now.");
            return View(model);
        }

        if (!model.ReturnToCalendar)
        {
            return RedirectToAction(nameof(Index));
        }

        // The week it is now on; for a cancelled one, the week it was on (ShownOn is from before the change).
        var shownOn = model.Change switch
        {
            OccurrenceChangeChoice.Move => FormInput.Date.Format(model.NewStart.Date),
            OccurrenceChangeChoice.Restore => FormInput.Date.Format(found.Date),
            _ => model.ShownOn,
        };
        return RedirectToAction(nameof(CalendarController.Index), "Calendar", new { date = shownOn });
    }

    // The occurrence on date (as the page sends it), with the series it belongs to; null when the activity doesn't
    // happen that day, or can't be read (edited by hand: the ordinary form shows it to fix).
    private static (Series Series, LocalDate Date)? OccurrenceOf(Activity activity, string? date)
    {
        return FormInput.ParseDate(date) is { } day && SeriesOf(activity) is { } series && series.Rule.FallsOn(series.FirstDate, day)
            ? (series, day)
            : null;
    }

    private static string OccurrenceFieldFor(CheckAspect aspect) => aspect switch
    {
        CheckAspect.Time => nameof(OccurrenceFormViewModel.NewStartTime),
        CheckAspect.Length => nameof(OccurrenceFormViewModel.DurationHours),
        _ => string.Empty,
    };

    // SCH-6, T16: other times later in its week for the occurrence its rule puts on date (a missed or skipped one, or
    // one to move), with nothing else moved. Each opens the occurrence's page, or a one-off's form, with that time
    // filled in, the pages a drag opens (CAL-8), so the move is checked and confirmed there (SCH-1).
    [HttpGet]
    public async Task<IActionResult> Reschedule(Guid id, string? date, bool returnToCalendar = false)
    {
        var activity = await FindAsync(id);
        if (activity is null || FormInput.ParseDate(date) is not { } day || SeriesOf(activity) is null)
        {
            return NotFound();
        }

        if (await weekPlanning.OptionsAsync(activity, day, most: 3) is not { } found)
        {
            return NotFound();
        }

        // Back where it was opened from once moved: the calendar, if it came from there.
        var occurrenceDate = FormInput.Date.Format(found.Occurrence.Date);
        var back = returnToCalendar ? "true" : null;
        var model = RescheduleViewModel.From(activity, found, (start, minutes) => activity.RepeatRule is null
            ? Url.Action(nameof(Edit), new { id, start, minutes, returnToCalendar = back })!
            : Url.Action(nameof(Occurrence), new { id, date = occurrenceDate, start, minutes, returnToCalendar = back })!);
        model.ReturnToCalendar = returnToCalendar;
        return View(model);
    }

    // Only asks; deleting needs the form post below.
    [HttpGet]
    public async Task<IActionResult> Delete(Guid id)
    {
        var activity = await FindAsync(id);
        ViewData[Clock] = (await PreferencesAsync()).TimesShownAs();
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
            ViewData[Clock] = (await PreferencesAsync()).TimesShownAs();
            return View(nameof(Delete), current);
        }

        return RedirectToAction(nameof(Index));
    }

    // Through the per-user filter: another user's activity is simply not found.
    private Task<Activity?> FindAsync(Guid id) => db.Activities.SingleOrDefaultAsync(activity => activity.Id == id);

    // What the form lists (locations) and the category defaults it hints at and fills blank buffers with (ACC-6).
    private async Task<ActivityFormViewModel> FillListsAsync(ActivityFormViewModel model)
    {
        model.Locations = await MyLocationsAsync();
        model.DefaultBuffers = await planning.DefaultBuffersAsync();
        return model;
    }

    private async Task<IReadOnlyList<Location>> MyLocationsAsync() =>
        (await db.Locations.AsNoTracking().ToListAsync())
            .OrderBy(location => location.Name, StringComparer.InvariantCultureIgnoreCase)
            .ToList();

    // The user's preferences (timezone and week start for a new activity), or the defaults before they have saved any.
    private async Task<UserPreferences> PreferencesAsync() =>
        await db.UserPreferences.AsNoTracking().SingleOrDefaultAsync() ?? new UserPreferences();

    // SCH-1: the one check's result, on the activity exactly as it would be saved (the whole activity, or for a change
    // to one occurrence, CAL-4, that occurrence). Each problem is shown by the field it is about. Clashes (H1) are
    // listed, and need "Save anyway" (§7.2, Q2). True when it can be saved.
    private async Task<bool> PassesTheCheckAsync(
        Activity activity, IConfirmsClashes model, ActivityCheckResult result, Func<CheckAspect, string>? fieldFor = null, bool oneOccurrence = false)
    {
        var zone = DateTimeZoneProviders.Tzdb[activity.TimeZoneId];
        var clock = (await PreferencesAsync()).TimesShownAs();
        var problems = result.Problems;
        foreach (var problem in problems)
        {
            ModelState.AddModelError((fieldFor ?? FieldFor)(problem.Aspect), problem.Message);
        }

        // Other activities that could only be checked in part: shown with the clashes, or on the list once saved.
        ViewData[CheckNotes] = result.Notes;
        if (problems.Count == 0 && result.Notes.Count > 0)
        {
            TempData[CheckNotes] = result.Notes.ToArray();
        }

        var clashes = result.Clashes.Select(clash => ActivityCheck.Describe(clash, zone, clock)).ToList();
        if (problems.Count > 0 || clashes.Count == 0)
        {
            return ModelState.IsValid;
        }

        // "Save anyway" only accepts the clashes the page showed: if the activity now clashes differently (it was
        // changed in the same submit, or another activity changed meanwhile), the new clashes are shown and asked about.
        var fingerprint = FingerprintOf(clashes);
        if (model.ConfirmClashes && model.ShownClashes == fingerprint)
        {
            return ModelState.IsValid;
        }

        ViewData["Clashes"] = clashes;
        TempData.Remove(CheckNotes);
        ModelState.Remove(nameof(model.ShownClashes));
        model.ShownClashes = fingerprint;
        ModelState.Remove(nameof(model.ConfirmClashes));
        model.ConfirmClashes = false;
        ModelState.AddModelError(nameof(model.ConfirmClashes), oneOccurrence
            ? "Where it now is, it clashes with your other activities, listed below. Change it, or tick \"Save anyway\" to keep the clashes."
            : $"It clashes with your other activities, listed below (checked from {result.Checked.Start.ToString("d MMMM uuuu", CultureInfo.InvariantCulture)} " +
              $"for {ActivityCheck.WeeksAhead} weeks). Change it, or tick \"Save anyway\" to keep the clashes.");
        return false;
    }

    // The same clashes always give the same text, in the same order (SCH-7), so the same fingerprint.
    private static string FingerprintOf(IEnumerable<string> clashes) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", clashes))));

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

        // The goal is a hidden link, not a choice: one that isn't the user's, or was deleted in another tab, is dropped
        // (as the database drops it), rather than stopping a save there's nothing on the page to correct.
        if (model.GoalId is { } goalId && !await db.WeeklyGoals.AnyAsync(goal => goal.Id == goalId))
        {
            model.GoalId = null;
        }
    }
}
