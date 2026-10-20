using FitnessTracker.Domain.Footprints;
using FitnessTracker.Domain.Recurrence;
using NodaTime;
using NodaTime.Text;

namespace FitnessTracker.Domain.Scheduling;

/// <summary>
/// One occurrence on the agenda. <see cref="Footprint"/> is the time it takes up, from getting ready to getting back
/// (null if that would be more than 24 hours, H8). <see cref="Note"/> says, in plain English, what couldn't be worked
/// out (travel, or the footprint): P1, said, never guessed. <see cref="ClashesWith"/>: the titles of what it overlaps.
/// </summary>
public sealed record AgendaItem(PlannedActivity Activity, Occurrence Occurrence, Footprint? Footprint, string? Note, IReadOnlyList<string> ClashesWith);

/// <summary>
/// GOL-2, first part: every occurrence starting on a run of days, in time order (then by title and id, SCH-7), for the home
/// page's "today and the next 7 days". Clashes (H1, CAL-6) are found on whole footprints, including activities from
/// just before or after those days, which aren't listed themselves.
/// </summary>
public static class Agenda
{
    /// <summary>
    /// Activities starting this many days either side of the dates looked at can still overlap them: two footprints of
    /// up to 24 hours each (H8) can reach 48 hours apart, which spans a third date across a 23-hour day. Used by the
    /// check on save too, and by callers deciding which activities to load.
    /// </summary>
    public const int MarginDays = 3;

    public static IReadOnlyList<AgendaItem> Between(IEnumerable<PlannedActivity> activities, PlanningContext context, LocalDate from, LocalDate through)
    {
        var entries = new List<(PlannedActivity Activity, Occurrence Occurrence, Footprint? Footprint, string? Note)>();
        foreach (var activity in activities)
        {
            var journeys = Travel.KnownJourneys(activity.LocationId, context);
            var travel = journeys ?? Journeys.None;
            var travelNote = journeys is null ? $"Travel isn't counted, because {Travel.WhyUnknown(activity.LocationId, context)}." : null;
            foreach (var occurrence in activity.Series.OccurrencesBetween(from.PlusDays(-MarginDays), through.PlusDays(MarginDays)))
            {
                var longest = Times.LongestFor(occurrence.Time, activity.Series.Zone);
                if (Footprint.ProblemsWith(occurrence.Time.Duration, activity.Buffers, travel, longest).Count > 0)
                {
                    entries.Add((activity, occurrence, null,
                        "With its preparation, travel and changing it would take up more than a whole day, so it isn't checked for clashes."));
                    continue;
                }

                entries.Add((activity, occurrence, Footprint.Around(occurrence.Time, activity.Buffers, travel, longest), travelNote));
            }
        }

        // One booking per entry with a footprint. A booking is equal only to itself (each has its own footprint), so it
        // finds its entry again; a clash adds the other one's title to both.
        var bookings = new Dictionary<Booking, int>();
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Footprint is { } footprint)
            {
                bookings[new Booking(entries[i].Activity.Title, footprint)] = i;
            }
        }

        var clashesWith = entries.Select(_ => new List<string>()).ToList();
        foreach (var clash in Conflicts.Among(bookings.Keys))
        {
            AddOnce(clashesWith[bookings[clash.Candidate]], clash.Other.Label);
            AddOnce(clashesWith[bookings[clash.Other]], clash.Candidate.Label);
        }

        return entries
            .Select((entry, i) => (Entry: entry, ClashesWith: clashesWith[i]))
            .Where(item => item.Entry.Occurrence.Time.Start.InZone(item.Entry.Activity.Series.Zone).Date is var date && date >= from && date <= through)
            .OrderBy(item => item.Entry.Occurrence.Time.Start)
            .ThenBy(item => item.Entry.Activity.Title, StringComparer.Ordinal)
            .ThenBy(item => item.Entry.Activity.Id)
            .ThenBy(item => item.Entry.Occurrence.Date)
            .Select(item => new AgendaItem(item.Entry.Activity, item.Entry.Occurrence, item.Entry.Footprint, item.Entry.Note, item.ClashesWith))
            .ToList();
    }

    /// <summary>The time it starts, saying which one when the clocks repeat it ("01:30 BST").</summary>
    public static string Starts(AgendaItem item) => Times.Clock(item.Occurrence.Time.Start.InZone(item.Activity.Series.Zone));

    /// <summary>
    /// When to go and when it's over, from the footprint: "Leave home 17:35, back 19:45" away from home (leaving is when
    /// the journey out starts, after getting ready at home, §7.1), "With getting ready and changing: 06:55 to 07:40" for
    /// buffers at home, or null. A time on another day says "the day before" or "the next day" (§8), and a time the
    /// clocks repeat says which one ("01:15 GMT").
    /// </summary>
    public static string? Trip(AgendaItem item)
    {
        if (item.Footprint is not { } footprint)
        {
            return null;
        }

        var zone = item.Activity.Series.Zone;
        var day = item.Occurrence.Time.Start.InZone(zone).Date;
        string At(Instant moment)
        {
            var local = moment.InZone(zone);
            var otherDay = local.Date < day ? " the day before" : local.Date > day ? " the next day" : "";
            return Times.Clock(local) + otherDay;
        }

        var travelOut = footprint.Segments.Where(segment => segment.Part == FootprintPart.TravelOut).Select(segment => (Instant?)segment.Time.Start).FirstOrDefault();
        var away = footprint.Segments.Any(segment => segment.Part is FootprintPart.TravelOut or FootprintPart.TravelBack);
        if (away)
        {
            return $"Leave home {At(travelOut ?? footprint.Activity.Start)}, back {At(footprint.Whole.End)}";
        }

        return footprint.Whole != footprint.Activity
            ? $"With getting ready and changing: {At(footprint.Whole.Start)} to {At(footprint.Whole.End)}"
            : null;
    }

    private static void AddOnce(List<string> titles, string title)
    {
        if (!titles.Contains(title))
        {
            titles.Add(title);
        }
    }
}

/// <summary>Travel around an activity, decided the same way for the check on save and the agenda (§7.1).</summary>
internal static class Travel
{
    /// <summary>None at home or at no particular place; the journeys when known; null when they can't be worked out.</summary>
    public static Journeys? KnownJourneys(Guid? locationId, PlanningContext context)
    {
        if (locationId is not { } at || at == context.Home)
        {
            return Journeys.None;
        }

        return context.Home is { } home ? context.Travel.JourneysFor(at, home).Journeys : null;
    }

    /// <summary>Why <see cref="KnownJourneys"/> is null, to finish a sentence: "none of your locations is marked as home".</summary>
    public static string WhyUnknown(Guid? locationId, PlanningContext context)
    {
        if (context.Home is not { } home || locationId is not { } at)
        {
            return "none of your locations is marked as home";
        }

        var missing = context.Travel.JourneysFor(at, home).MissingLegs.Select(leg => $"from {NameOf(leg.From, context)} to {NameOf(leg.To, context)}");
        return $"there's no travel time {string.Join(", or ", missing)}";
    }

    public static string NameOf(Guid location, PlanningContext context) =>
        context.Places.TryGetValue(location, out var place) ? place.Name : "that location";
}

/// <summary>Times and dates as the explanations write them.</summary>
internal static class Times
{
    public static readonly LocalDatePattern Date = LocalDatePattern.CreateWithInvariantCulture("ddd d MMM uuuu");

    public static readonly LocalTimePattern Time = LocalTimePattern.CreateWithInvariantCulture("HH:mm");

    /// <summary>H8's limit for an occurrence: 24 hours, or the whole day it starts on if that day is longer (Q7).</summary>
    public static Duration LongestFor(Interval occurrence, DateTimeZone zone) =>
        Footprint.LongestOn(occurrence.Start.InZone(zone).Date, zone);

    /// <summary>"07:30", or "01:15 BST" for a time the clocks repeat on the night they go back.</summary>
    public static string Clock(ZonedDateTime moment) =>
        moment.Zone.MapLocal(moment.LocalDateTime).Count > 1
            ? $"{Time.Format(moment.TimeOfDay)} {moment.GetZoneInterval().Name}"
            : Time.Format(moment.TimeOfDay);
}
