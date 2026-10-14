using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FitnessTracker.Web.Controllers;

// ACC-1: register, sign in and sign out. The MVP sends no email, so there is no confirmation or
// emailed password reset (a documented limitation).
public class AccountController(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    IOptions<IdentityOptions> identityOptions) : Controller
{
    // The same message whether the email is unknown or the password is wrong. (Registration and the
    // lockout message can still reveal that an address has an account: a known MVP limitation.)
    private const string SignInFailed = "The email address or password is incorrect.";

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Register() => View();

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // Phone keyboards add a space after an autocompleted address.
        var email = model.Email.Trim();
        var user = new AppUser { UserName = email, Email = email };
        var result = await userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var (field, message) in result.Errors.Select(Describe).Distinct())
            {
                ModelState.AddModelError(field, message);
            }

            return View(model);
        }

        await signInManager.SignInAsync(user, isPersistent: false);
        return RedirectToAction(nameof(HomeController.Index), "Home");
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await signInManager.PasswordSignInAsync(
            model.Email.Trim(), model.Password, model.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            // Only addresses on this site, so a crafted link can't send someone elsewhere after signing in.
            return Url.IsLocalUrl(returnUrl)
                ? LocalRedirect(returnUrl)
                : RedirectToAction(nameof(HomeController.Index), "Home");
        }

        ModelState.AddModelError(string.Empty, result.IsLockedOut ? LockedOutMessage() : SignInFailed);
        return View(model);
    }

    // POST only (with the anti-forgery token), so a link or image elsewhere can't sign someone out.
    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    private string LockedOutMessage() =>
        $"Too many attempts. Wait {identityOptions.Value.Lockout.DefaultLockoutTimeSpan.TotalMinutes:0} minutes, then try again.";

    /// <summary>Which form field an Identity error belongs to, and a plain-English message for it.</summary>
    private static (string Field, string Message) Describe(IdentityError error) => error.Code switch
    {
        // The user name is the email address, so Identity reports a clash twice and says "user name".
        nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName)
            => (nameof(RegisterViewModel.Email), "An account with this email address already exists. Sign in instead."),
        nameof(IdentityErrorDescriber.InvalidEmail) or nameof(IdentityErrorDescriber.InvalidUserName)
            => (nameof(RegisterViewModel.Email), "Enter a valid email address."),
        _ when error.Code.StartsWith("Password", StringComparison.Ordinal)
            => (nameof(RegisterViewModel.Password), error.Description),
        _ => (string.Empty, error.Description),
    };
}
