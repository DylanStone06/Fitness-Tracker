using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Services;
using NodaTime;
using NodaTime.Text;

namespace FitnessTracker.Web.Models.Home;

/// <summary>One thing to set up before the plan can be trusted, with where to do it.</summary>
public sealed record SetupStep(string Text, bool Done, string Controller, string Action = "Index", string? Progress = null);

/// <summary>
/// The home page (GOL-2, first part): a greeting, what is still to set up, the next activity, and the agenda for today
/// and the next 7 days. The rest of GOL-2 (free time, tonight's sleep, weekly targets) comes with the phases that
/// build those.
/// </summary>
public sealed class HomeViewModel
{
    public const int DaysAhead = 7;

    /// <summary>How the page names a day: "Thu 22 Oct".</summary>
    public static readonly LocalDatePattern DayPattern = LocalDatePattern.CreateWithInvariantCulture("ddd d MMM");

    public required string Greeting { get; init; }

    public required IReadOnlyList<SetupStep> Steps { get; init; }

    public required LocalDate Today { get; init; }

    public required IReadOnlyList<AgendaItem> Agenda { get; init; }

    /// <summary>The next activity (GOL-2), or null when nothing listed is left.</summary>
    public required NextUp? Next { get; init; }

    /// <summary>When <see cref="Next"/> is: "Under way", "Today", "Tomorrow" or its date ("Thu 22 Oct").</summary>
    public required string? NextWhen { get; init; }

    /// <summary>Each of the user's activities' category, for its colour and name (CAL-2).</summary>
    public required IReadOnlyDictionary<Guid, ActivityCategory> Categories { get; init; }

    public required Guid? Home { get; init; }

    public required IReadOnlyDictionary<Guid, string> PlaceNames { get; init; }

    /// <summary>Stored activities the app couldn't read, so they aren't in the agenda.</summary>
    public required IReadOnlyList<string> Unreadable { get; init; }

    public bool StillToSetUp => Steps.Any(step => !step.Done);

    public IEnumerable<AgendaItem> On(LocalDate date) =>
        Agenda.Where(item => item.Occurrence.Time.Start.InZone(item.Activity.Series.Zone).Date == date);

    /// <summary>"Good morning" from 05:00, "Good afternoon" from 12:00, "Good evening" from 18:00 until 05:00.</summary>
    public static string GreetingAt(LocalTime time) => time.Hour switch
    {
        >= 5 and < 12 => "Good morning",
        >= 12 and < 18 => "Good afternoon",
        _ => "Good evening",
    };

    /// <summary>
    /// When the next activity is, by the date it starts in its own timezone, the same date the agenda lists it under.
    /// </summary>
    public static string When(NextUp next, LocalDate today)
    {
        if (next.UnderWay)
        {
            return "Under way";
        }

        var date = next.Item.Occurrence.Time.Start.InZone(next.Item.Activity.Series.Zone).Date;
        return date == today ? "Today"
            : date == today.PlusDays(1) ? "Tomorrow"
            : DayPattern.Format(date);
    }

    public static HomeViewModel From(UserPlanning user, IReadOnlyList<AgendaItem> agenda, IReadOnlyDictionary<Guid, ActivityCategory> categories, IReadOnlyList<string> unreadable)
    {
        // Qualified: inside this class, "Agenda" is the property.
        var next = Domain.Scheduling.Agenda.Next(agenda, user.Now.ToInstant());
        var home = user.Context.Home;

        // Both journeys to and from every place away from home (§7.1): a missing one is never taken as zero.
        var awayFromHome = user.Locations.Where(location => location.Id != home).Select(location => location.Id).ToList();
        var needed = awayFromHome.Count * 2;
        var present = home is { } at
            ? awayFromHome.Sum(place => user.Legs.Count(leg => (leg.FromLocationId == at && leg.ToLocationId == place) || (leg.FromLocationId == place && leg.ToLocationId == at)))
            : 0;

        return new HomeViewModel
        {
            Steps =
            [
                new("Set your timezone and sleep times", user.PreferencesSaved, "Preferences"),
                new("Mark your home", home is not null, "Locations"),
                new("Add travel times to your other places", home is not null && present == needed, "TravelLegs",
                    Progress: home is null ? "Mark your home first" : needed > 0 ? $"{present} of {needed} journeys" : null),
                new("Add an activity", categories.Count > 0, "Activities", "Create"),
            ],
            Greeting = GreetingAt(user.Now.TimeOfDay),
            Today = user.Context.Today,
            Agenda = agenda,
            Next = next,
            NextWhen = next is null ? null : When(next, user.Context.Today),
            Categories = categories,
            Home = home,
            PlaceNames = user.Locations.ToDictionary(location => location.Id, location => location.Name),
            Unreadable = unreadable,
        };
    }
}
