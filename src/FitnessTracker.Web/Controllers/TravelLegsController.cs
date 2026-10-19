using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models.Locations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitnessTracker.Web.Controllers;

// TRV-2: how long each one-way journey between two of the signed-in user's locations takes. The per-user filter
// means another user's leg is "not found" (T19), and a leg can only join the signed-in user's own locations.
public class TravelLegsController(AppDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var locations = await MyLocationsAsync();
        var names = locations.ToDictionary(location => location.Id, location => location.Name);
        var legs = await db.TravelLegs.AsNoTracking().ToListAsync();
        ViewData["Names"] = names;
        return View(legs
            .OrderBy(leg => names.GetValueOrDefault(leg.FromLocationId), StringComparer.InvariantCultureIgnoreCase)
            .ThenBy(leg => names.GetValueOrDefault(leg.ToLocationId), StringComparer.InvariantCultureIgnoreCase)
            .ToList());
    }

    [HttpGet]
    public async Task<IActionResult> Create() => View(new TravelLegFormViewModel { Locations = await MyLocationsAsync() });

    [HttpPost]
    public async Task<IActionResult> Create(TravelLegFormViewModel model)
    {
        await CheckFormAsync(model, exceptId: null);
        if (!ModelState.IsValid)
        {
            model.Locations = await MyLocationsAsync();
            return View(model);
        }

        var from = model.FromLocationId!.Value;
        var to = model.ToLocationId!.Value;
        db.TravelLegs.Add(new TravelLeg { FromLocationId = from, ToLocationId = to, Minutes = model.Minutes, Mode = model.Mode });

        // The return journey is added only if it doesn't have a time yet: an existing one may differ on purpose.
        if (model.AlsoAddReturnJourney && !await db.TravelLegs.AnyAsync(leg => leg.FromLocationId == to && leg.ToLocationId == from))
        {
            db.TravelLegs.Add(new TravelLeg { FromLocationId = to, ToLocationId = from, Minutes = model.Minutes, Mode = model.Mode });
        }

        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        var leg = await FindAsync(id);
        if (leg is null)
        {
            return NotFound();
        }

        var model = TravelLegFormViewModel.From(leg);
        model.Locations = await MyLocationsAsync();
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(Guid id, TravelLegFormViewModel model)
    {
        var leg = await FindAsync(id);
        if (leg is null)
        {
            return NotFound();
        }

        await CheckFormAsync(model, exceptId: id);
        if (!ModelState.IsValid)
        {
            model.Locations = await MyLocationsAsync();
            return View(model);
        }

        // Saved only if nobody has saved this leg since the form was opened (requirements §9).
        db.Entry(leg).Property(saved => saved.Version).OriginalValue = model.Version;
        leg.FromLocationId = model.FromLocationId!.Value;
        leg.ToLocationId = model.ToLocationId!.Value;
        leg.Minutes = model.Minutes;
        leg.Mode = model.Mode;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            var current = await db.TravelLegs.AsNoTracking().SingleOrDefaultAsync(saved => saved.Id == id);
            if (current is null)
            {
                return NotFound();
            }

            ModelState.AddModelError(string.Empty,
                $"This journey was changed in another tab or window after you opened this page. It now takes " +
                $"{current.Minutes} minutes. Check the details below, then save again to replace them.");
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
        var leg = await FindAsync(id);
        if (leg is null)
        {
            return NotFound();
        }

        ViewData["Names"] = (await MyLocationsAsync()).ToDictionary(location => location.Id, location => location.Name);
        return View(leg);
    }

    [HttpPost]
    [ActionName(nameof(Delete))]
    public async Task<IActionResult> DeleteConfirmed(Guid id)
    {
        var leg = await FindAsync(id);
        if (leg is null)
        {
            return NotFound();
        }

        db.TravelLegs.Remove(leg);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    // Through the per-user filter: another user's leg is simply not found.
    private Task<TravelLeg?> FindAsync(Guid id) => db.TravelLegs.SingleOrDefaultAsync(leg => leg.Id == id);

    private async Task<IReadOnlyList<Location>> MyLocationsAsync() =>
        (await db.Locations.AsNoTracking().ToListAsync())
            .OrderBy(location => location.Name, StringComparer.InvariantCultureIgnoreCase)
            .ToList();

    // Two different places, both the signed-in user's own (an id from anywhere else is refused, even if it is a real
    // location), a defined mode, and no second time for the same direction.
    private async Task CheckFormAsync(TravelLegFormViewModel model, Guid? exceptId)
    {
        if (!Enum.IsDefined(model.Mode))
        {
            ModelState.AddModelError(nameof(model.Mode), "Choose how you travel from the list.");
        }

        if (model.FromLocationId is not { } from || model.ToLocationId is not { } to)
        {
            return;
        }

        var mine = await db.Locations.Where(location => location.Id == from || location.Id == to).Select(location => location.Id).ToListAsync();
        if (!mine.Contains(from))
        {
            ModelState.AddModelError(nameof(model.FromLocationId), "Choose a location from the list.");
        }

        if (!mine.Contains(to))
        {
            ModelState.AddModelError(nameof(model.ToLocationId), "Choose a location from the list.");
        }

        if (from == to)
        {
            ModelState.AddModelError(nameof(model.ToLocationId), "Choose two different places: there's no journey from a place to itself.");
        }
        else if (await db.TravelLegs.AnyAsync(leg => leg.FromLocationId == from && leg.ToLocationId == to && leg.Id != exceptId))
        {
            ModelState.AddModelError(nameof(model.ToLocationId), "That journey already has a time. Edit it from the list instead.");
        }
    }
}
