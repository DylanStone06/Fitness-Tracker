using FitnessTracker.Web.Models;
using FitnessTracker.Web.Models.Goals;
using FitnessTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using NodaTime;

namespace FitnessTracker.Web.Controllers;

// SCH-3 to SCH-5: "Plan my week": times for the sessions the signed-in user's weekly goals still need. It only reads
// (through the per-user filter, T19); adding a session posts the ordinary activity form, so it is checked like any
// save (SCH-1). Times said no to are carried in the address ("skip"), not stored.
public class PlanController(WeekPlanning planning) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? date, string[]? skip)
    {
        var day = FormInput.ParseDate(date) is { } parsed && FormInput.IsPlannable(parsed) ? parsed : (LocalDate?)null;
        var skips = (skip ?? []).Where(text => PlanPageViewModel.ParseSkip(text) is not null).ToList();
        var rejected = skips.Select(text => PlanPageViewModel.ParseSkip(text)!.Value).ToList();
        return View(PlanPageViewModel.From(await planning.PlanAsync(day, rejected), skips));
    }
}
