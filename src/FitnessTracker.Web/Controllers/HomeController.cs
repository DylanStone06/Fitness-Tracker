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
    // GOL-2, first part: what is still to set up, and today and the next 7 days. Everything is the signed-in user's
    // own, through the per-user filter (T19, HomeTests).
    public async Task<IActionResult> Index()
    {
        var user = await planning.LoadAsync();
        var today = user.Context.Today;
        var unreadable = new List<string>();
        // A few days either side, so clashes with activities just before or after the week still show.
        var activities = await planning.ActivitiesOnAsync(new DateInterval(today.PlusDays(-3), today.PlusDays(HomeViewModel.DaysAhead + 3)), unreadable);
        var agenda = Agenda.Between(activities, user.Context, today, today.PlusDays(HomeViewModel.DaysAhead));
        return View(HomeViewModel.From(user, agenda, await db.Activities.AnyAsync(), unreadable));
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
