using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models.Locations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitnessTracker.Web.Controllers;

// TRV-1, first version: the signed-in user's locations, by name only (opening hours and travel legs arrive in
// Phase 4). AppDbContext only ever returns the signed-in user's rows, so another user's location is simply not
// found, exactly like an id that doesn't exist (T19, ACC-2).
public class LocationsController(AppDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var locations = await db.Locations.ToListAsync();
        // Sorted here rather than by SQLite, which puts every capital letter before every lower-case one.
        return View(locations.OrderBy(location => location.Name, StringComparer.InvariantCultureIgnoreCase).ToList());
    }

    [HttpGet]
    public IActionResult Create() => View(new LocationFormViewModel());

    [HttpPost]
    public async Task<IActionResult> Create(LocationFormViewModel model)
    {
        await CheckNameIsFreeAsync(model, exceptId: null);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        db.Locations.Add(new Location { Name = model.Name });
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        var location = await db.Locations.FindAsync(id);
        return location is null ? NotFound() : View(new LocationFormViewModel { Name = location.Name });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(Guid id, LocationFormViewModel model)
    {
        var location = await db.Locations.FindAsync(id);
        if (location is null)
        {
            return NotFound();
        }

        await CheckNameIsFreeAsync(model, exceptId: id);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        location.Name = model.Name;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    // Only asks; deleting needs the form post below.
    [HttpGet]
    public async Task<IActionResult> Delete(Guid id)
    {
        var location = await db.Locations.FindAsync(id);
        return location is null ? NotFound() : View(location);
    }

    [HttpPost]
    [ActionName(nameof(Delete))]
    public async Task<IActionResult> DeleteConfirmed(Guid id)
    {
        var location = await db.Locations.FindAsync(id);
        if (location is null)
        {
            return NotFound();
        }

        db.Locations.Remove(location);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    // Two locations with the same name couldn't be told apart when choosing one for an activity. Capitals are
    // ignored, as in the list's order; names only need to differ within one person's locations (the filter).
    private async Task CheckNameIsFreeAsync(LocationFormViewModel model, Guid? exceptId)
    {
        if (!ModelState.IsValid)
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
