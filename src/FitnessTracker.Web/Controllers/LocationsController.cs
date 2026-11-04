using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models.Locations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitnessTracker.Web.Controllers;

// TRV-1: the signed-in user's locations: name, whether it is home, and opening hours. AppDbContext only ever
// returns the signed-in user's rows, so another user's location is simply not found, exactly like an id that
// doesn't exist (T19, ACC-2).
public class LocationsController(AppDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var locations = await db.Locations.Include(location => location.OpeningTimes).ToListAsync();
        // Sorted here rather than by SQLite, which puts every capital letter before every lower-case one.
        return View(locations.OrderBy(location => location.Name, StringComparer.InvariantCultureIgnoreCase).ToList());
    }

    [HttpGet]
    public IActionResult Create() => View(new LocationFormViewModel());

    [HttpPost]
    public async Task<IActionResult> Create(LocationFormViewModel model)
    {
        await CheckFormAsync(model, exceptId: null);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var location = new Location();
        await ApplyAsync(model, location);
        db.Locations.Add(location);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        var location = await FindAsync(id);
        return location is null ? NotFound() : View(LocationFormViewModel.From(location));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(Guid id, LocationFormViewModel model)
    {
        var location = await FindAsync(id);
        if (location is null)
        {
            return NotFound();
        }

        await CheckFormAsync(model, exceptId: id);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        await ApplyAsync(model, location);
        // Marked changed even when only its opening times changed, so it gets a new version and a tab opened before
        // this save can't overwrite it. Saved only if nobody has saved this location since the form was opened (§9).
        db.Entry(location).State = EntityState.Modified;
        db.Entry(location).Property(saved => saved.Version).OriginalValue = model.Version;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ChangedElsewhereAsync(id, model);
        }

        return RedirectToAction(nameof(Index));
    }

    // Only asks; deleting needs the form post below.
    [HttpGet]
    public async Task<IActionResult> Delete(Guid id)
    {
        var location = await FindAsync(id);
        return location is null ? NotFound() : await DeleteViewAsync(location);
    }

    /// <summary>ViewData: the names of the routines based at the location about to be deleted (TRV-4), in name order.</summary>
    public const string RoutinesBasedHere = "RoutinesBasedHere";

    // The delete page, saying which routines are based there: they are based at home again (AppDbContext: set null).
    private async Task<ViewResult> DeleteViewAsync(Location location)
    {
        ViewData[RoutinesBasedHere] = (await db.Routines.AsNoTracking().Where(routine => routine.BaseLocationId == location.Id).Select(routine => routine.Name).ToListAsync())
            .Order(StringComparer.InvariantCultureIgnoreCase)
            .ToList();
        return View(nameof(Delete), location);
    }

    // version: the one the delete page was opened with. If another tab has changed the location since, it isn't
    // deleted; the page asks again, naming it as it is now (requirements §9). Its opening times go with it (cascade).
    [HttpPost]
    [ActionName(nameof(Delete))]
    public async Task<IActionResult> DeleteConfirmed(Guid id, Guid version)
    {
        var location = await FindAsync(id);
        if (location is null)
        {
            return NotFound();
        }

        db.Entry(location).Property(saved => saved.Version).OriginalValue = version;
        db.Locations.Remove(location);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            var current = await db.Locations.AsNoTracking().SingleOrDefaultAsync(saved => saved.Id == id);
            if (current is null)
            {
                return NotFound();
            }

            ModelState.AddModelError(string.Empty,
                $"This location was changed in another tab or window after you opened this page. It's now called " +
                $"{current.Name}. Delete it anyway?");
            return await DeleteViewAsync(current);
        }

        return RedirectToAction(nameof(Index));
    }

    // Through the per-user filter, with its opening times.
    private Task<Location?> FindAsync(Guid id) =>
        db.Locations.Include(location => location.OpeningTimes).SingleOrDefaultAsync(location => location.Id == id);

    // Copies the form onto the location: its opening times are replaced, and marking it as home unmarks any other
    // home, because travel is measured from one place (§7.1).
    private async Task ApplyAsync(LocationFormViewModel model, Location location)
    {
        location.Name = model.Name;
        location.IsHome = model.IsHome;
        location.HasOpeningHours = model.HasOpeningHours;
        db.LocationOpeningTimes.RemoveRange(location.OpeningTimes);
        // Added explicitly: new rows already have ids, so EF would otherwise take them for existing rows being
        // changed (and the ownership check would rightly refuse them).
        var openingTimes = model.OpeningTimesToStore().ToList();
        foreach (var time in openingTimes)
        {
            time.LocationId = location.Id;
        }

        db.LocationOpeningTimes.AddRange(openingTimes);
        location.OpeningTimes = openingTimes;

        if (model.IsHome)
        {
            var otherHomes = await db.Locations.Where(other => other.IsHome && other.Id != location.Id).ToListAsync();
            foreach (var other in otherHomes)
            {
                other.IsHome = false;
            }
        }
    }

    // Another tab saved this location after the form was opened. Read it again through the per-user filter: gone
    // (deleted in another tab) is "not found", like any missing location. Otherwise say what it is now, keep what
    // was typed, and give the form the latest version, so saving again is a deliberate choice.
    private async Task<IActionResult> ChangedElsewhereAsync(Guid id, LocationFormViewModel model)
    {
        var current = await db.Locations.AsNoTracking().SingleOrDefaultAsync(location => location.Id == id);
        if (current is null)
        {
            return NotFound();
        }

        ModelState.AddModelError(string.Empty,
            $"This location was changed in another tab or window after you opened this page. It's now called " +
            $"{current.Name}. Check the details below, then save again to replace them.");
        // The hidden field shows what was posted unless the old value is cleared.
        ModelState.Remove(nameof(model.Version));
        model.Version = current.Version;
        return View(model);
    }

    // Problems across the form (listed with each field's own, all at once), and a name that is already used: two
    // locations with the same name couldn't be told apart when choosing one for an activity. Capitals are ignored,
    // as in the list's order; names only need to differ within one person's locations (the filter).
    private async Task CheckFormAsync(LocationFormViewModel model, Guid? exceptId)
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

        var otherNames = await db.Locations.Where(location => location.Id != exceptId).Select(location => location.Name).ToListAsync();
        if (otherNames.Contains(model.Name, StringComparer.InvariantCultureIgnoreCase))
        {
            ModelState.AddModelError(nameof(model.Name), $"You already have a location called {model.Name}. Choose a different name.");
        }
    }
}
