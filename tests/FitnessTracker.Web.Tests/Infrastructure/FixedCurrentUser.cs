using FitnessTracker.Web.Services;

namespace FitnessTracker.Web.Tests.Infrastructure;

/// <summary>Stands in for "who is signed in" when a test builds a database context directly.</summary>
public sealed class FixedCurrentUser(string? userId) : ICurrentUser
{
    public string? UserId => userId;
}
