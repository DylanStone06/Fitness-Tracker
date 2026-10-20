using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models;
using FitnessTracker.Web.Models.Home;
using FitnessTracker.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace FitnessTracker.Web.Controllers;

public class HomeController(AppDbContext db, PlanningData planning) : Controller
{
    // GOL-2, first part: a greeting, what is still to set up, the next activity, and today and the next 7 days.
    // Everything is the signed-in user's own, through the per-user filter (T19, HomeTests).
    public async Task<IActionResult> Index()
    {
        var user = await planning.LoadAsync();
        var today = user.Context.Today;
        var unreadable = new List<string>();
        // A few days either side, so clashes with activities just before or after the week still show.
        var loaded = new DateInterval(today.PlusDays(-Agenda.MarginDays), today.PlusDays(HomeViewModel.DaysAhead + Agenda.MarginDays));
        var activities = await planning.ActivitiesOnAsync(loaded, unreadable);
        var agenda = Agenda.Between(activities, user.Context, today, today.PlusDays(HomeViewModel.DaysAhead));
        // Two columns, not whole rows: every activity's category, for its colour and name (CAL-2).
        var categories = await db.Activities
            .Select(activity => new { activity.Id, activity.Category })
            .ToDictionaryAsync(activity => activity.Id, activity => activity.Category);
        return View(HomeViewModel.From(user, agenda, categories, unreadable));
    }

    // Error and Status are re-run for whichever request failed, which may be a signed-out POST whose
    // anti-forgery token was the problem. So both are open to everyone, accept any method and skip that check.
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    // Shown for responses that have no page of their own: 404 "not found" and 400 "form rejected".
    // The original status code is kept (see UseStatusCodePagesWithReExecute in Program.cs).
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Status(int code)
    {
        return View(new StatusPageViewModel(code));
    }
}
