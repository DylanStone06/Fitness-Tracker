using System.Diagnostics;
using FitnessTracker.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FitnessTracker.Web.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    // Error and Status are re-run for whichever request failed, which may be a signed-out POST whose
    // anti-forgery token was the problem. So both are open to everyone, accept any method and skip that check.
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
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
