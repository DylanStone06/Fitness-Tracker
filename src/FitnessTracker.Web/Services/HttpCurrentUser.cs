using System.Security.Claims;

namespace FitnessTracker.Web.Services;

/// <summary>Reads the signed-in user from the current web request (Identity puts the user id in NameIdentifier).</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public string? UserId => httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
}
