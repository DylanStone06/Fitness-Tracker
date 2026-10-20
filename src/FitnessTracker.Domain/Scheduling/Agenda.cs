using FitnessTracker.Domain.Footprints;
using FitnessTracker.Domain.Recurrence;
using NodaTime;

namespace FitnessTracker.Domain.Scheduling;

/// <summary>
/// One occurrence on the agenda. <see cref="Whole"/> is its footprint, from getting ready to getting back (null if it
/// would take up more than 24 hours, H8). <see cref="TravelUnknown"/>: its travel couldn't be worked out, so the
/// footprint is without it (P1: said, never guessed). <see cref="ClashesWith"/>: the titles of what it overlaps.
/// </summary>
public sealed record AgendaItem(PlannedActivity Activity, Occurrence Occurrence, Interval? Whole, bool TravelUnknown, IReadOnlyList<string> ClashesWith);

/// <summary>
/// GOL-2, first part: every occurrence starting on a run of days, in time order (then by title, SCH-7), for the home
/// page's "today and the next 7 days". Clashes (H1, CAL-6) are found on whole footprints, including activities from
/// just before or after those days, which aren't listed themselves.
/// </summary>
public static class Agenda
{
    // As in ActivityCheck: two footprints of up to 24 hours can reach a third date across a 23-hour day.
    private const int MarginDays = 3;

    public static IReadOnlyList<AgendaItem> Between(IEnumerable<PlannedActivity> activities, PlanningContext context, LocalDate from, LocalDate through)
    {
        var entries = new List<(PlannedActivity Activity, Occurrence Occurrence, Footprint? Footprint, bool TravelUnknown)>();
        foreach (var activity in activities)
        {
            var journeys = Travel.KnownJourneys(activity.LocationId, context);
            var travel = journeys ?? Journeys.None;
            foreach (var occurrence in activity.Series.OccurrencesBetween(from.PlusDays(-MarginDays), through.PlusDays(MarginDays)))
            {
                var footprint = Footprint.ProblemsWith(occurrence.Time.Duration, activity.Buffers, travel).Count == 0
                    ? Footprint.Around(occurrence.Time, activity.Buffers, travel)
                    : null;
                entries.Add((activity, occurrence, footprint, journeys is null));
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
            .Select(item => new AgendaItem(item.Entry.Activity, item.Entry.Occurrence, item.Entry.Footprint?.Whole, item.Entry.TravelUnknown, item.ClashesWith))
            .ToList();
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
}
