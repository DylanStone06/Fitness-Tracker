using Microsoft.AspNetCore.Identity;

namespace FitnessTracker.Web.Data;

/// <summary>
/// Someone with an account. A subclass from the start, so profile fields (Phase 4 preferences) can be
/// added later without changing Identity's types.
/// </summary>
public class AppUser : IdentityUser;
