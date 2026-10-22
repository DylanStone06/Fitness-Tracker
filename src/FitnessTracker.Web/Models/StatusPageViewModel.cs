namespace FitnessTracker.Web.Models;

/// <summary>What to tell someone when a request ends in an error status with no page of its own.</summary>
public sealed record StatusPageViewModel(int StatusCode)
{
    public string Heading => StatusCode switch
    {
        404 => "Page not found",
        400 => "That form couldn't be sent",
        _ => "Something went wrong",
    };

    public string Advice => StatusCode switch
    {
        404 => "Check the address, or go back to the home page.",
        400 => "The page may have been open a long time, or you signed in or out in another tab. "
            + "Go back, refresh the page and try again.",
        _ => "Please try again.",
    };
}
