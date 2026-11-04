using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models.Preferences;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitnessTracker.Web.Controllers;

// ACC-6: the signed-in user's default getting-ready and changing times per category. There is no id in the address:
// the per-user filter means this only ever reads or changes the signed-in user's own rows (T19, CategoryBuffersTests).
public class CategoryBuffersController(AppDbContext db) : Controller
{
    private const string Saved = "Saved";

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewData[Saved] = TempData[Saved];
        return View(CategoryBuffersFormViewModel.From(await db.CategoryBuffers.AsNoTracking().ToListAsync()));
    }

    [HttpPost]
    public async Task<IActionResult> Index(CategoryBuffersFormViewModel model)
    {
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

        var stored = await db.CategoryBuffers.ToListAsync();
        foreach (var row in model.Rows)
        {
            if (stored.FirstOrDefault(saved => saved.Category == row.Category) is { } saved)
            {
                // Saved only if nobody has saved this row since the form was opened (requirements §9).
                db.Entry(saved).Property(buffers => buffers.Version).OriginalValue = row.Version;
                saved.PreparationMinutes = row.PreparationMinutes;
                saved.ChangingMinutes = row.ChangingMinutes;
            }
            else
            {
                db.CategoryBuffers.Add(new CategoryBuffers { Category = row.Category, PreparationMinutes = row.PreparationMinutes, ChangingMinutes = row.ChangingMinutes });
            }
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Another tab saved since this form was opened: a version that no longer matches, or a first save of the
            // same category at once (refused by the one-row-per-category index). Keep what was typed, give the form the
            // latest versions, and ask: saving again is then a deliberate choice.
            db.ChangeTracker.Clear();
            var current = await db.CategoryBuffers.AsNoTracking().ToListAsync();
            for (var i = 0; i < model.Rows.Count; i++)
            {
                ModelState.Remove($"Rows[{i}].Version");
                model.Rows[i].Version = current.FirstOrDefault(saved => saved.Category == model.Rows[i].Category)?.Version ?? Guid.Empty;
            }

            ModelState.AddModelError(string.Empty,
                "These defaults were changed in another tab or window after you opened this page. Check the times below, then save again to replace them.");
            return View(model);
        }

        TempData[Saved] = true;
        return RedirectToAction(nameof(Index));
    }
}
