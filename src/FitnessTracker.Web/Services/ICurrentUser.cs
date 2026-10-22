namespace FitnessTracker.Web.Services;

/// <summary>Who is signed in for the current request.</summary>
public interface ICurrentUser
{
    /// <summary>The signed-in user's id, or null when nobody is signed in.</summary>
    string? UserId { get; }
}
