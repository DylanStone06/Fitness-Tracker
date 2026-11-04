using FitnessTracker.Web.Data;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace FitnessTracker.Web.Services;

/// <summary>
/// ACC-3, Appearance: the theme the layout draws each page in, so it arrives that way and never flashes the other one.
/// The signed-in user's choice, through the per-user filter; dark until they choose (the owner's default, 20 October
/// 2026), and dark when signed out. The error page doesn't read it: it may be showing a database fault.
/// </summary>
public class UserAppearance(AppDbContext db)
{
    public async Task<Appearance> CurrentAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true || context.Features.Get<IExceptionHandlerFeature>() is not null)
        {
            return Appearance.Dark;
        }

        return await db.UserPreferences.AsNoTracking().Select(preferences => (Appearance?)preferences.Appearance).SingleOrDefaultAsync()
            ?? Appearance.Dark;
    }
}
