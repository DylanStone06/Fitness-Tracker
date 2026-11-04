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
        var activities = await planning.ActivitiesOnAsync(loaded, user.Routines.Calendar, unreadable);
        var agenda = Agenda.Between(activities, user.Context, today, today.PlusDays(HomeViewModel.DaysAhead));
        // A few columns, not whole rows: every activity's category, for its colour and name (CAL-2), and whether it
        // repeats, so an occurrence opens its own page (CAL-4).
        var kinds = await db.Activities
            .Select(activity => new { activity.Id, activity.Category, Repeats = activity.RepeatRule != null })
            .ToListAsync();
        return View(HomeViewModel.From(
            user,
            agenda,
            kinds.ToDictionary(kind => kind.Id, kind => kind.Category),
            kinds.Where(kind => kind.Repeats).Select(kind => kind.Id).ToHashSet(),
            unreadable));
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
