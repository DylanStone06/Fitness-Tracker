using FitnessTracker.Domain.Footprints;
using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Domain.Time;
using NodaTime;
using NodaTime.Text;

namespace FitnessTracker.Domain.Scheduling;

/// <summary>
/// One occurrence on the agenda. <see cref="Footprint"/> is the time it takes up, from getting ready to getting back
/// (null if that would be longer than the day allows, H8). <see cref="Note"/> says, in plain English, what couldn't be worked
/// out (travel, or the footprint): P1, said, never guessed. <see cref="ClashesWith"/>: the titles of what it overlaps.
/// </summary>
public sealed record AgendaItem(PlannedActivity Activity, Occurrence Occurrence, Footprint? Footprint, string? Note, IReadOnlyList<string> ClashesWith);

/// <summary>The agenda item to do next (GOL-2), and whether it has begun: getting ready or the journey out counts.</summary>
public sealed record NextUp(AgendaItem Item, bool UnderWay);

/// <summary>
/// GOL-2, first part: every occurrence starting on a run of days, in time order (then by title and id, SCH-7), for the home
/// page's "today and the next 7 days". Clashes (H1, CAL-6) are found on whole footprints, including activities from
/// just before or after those days, which aren't listed themselves.
/// </summary>
public static class Agenda
{
    /// <summary>
    /// Activities starting this many days either side of the dates looked at can still overlap them: two footprints of
    /// up to a day each (H8: 24 hours, or 25 on the day the clocks go back) can reach 49 hours apart, which spans a third date across a 23-hour day. Used by the
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
                        "Counting getting ready, travel and changing, it would take up more than a day allows, so it isn't checked for clashes."));
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

    /// <summary>
    /// GOL-2's "the next activity": of the items not over at <paramref name="now"/>, the one you start getting ready or
    /// set off for first; ties go in the agenda's own order, whatever order the items come in (SCH-7). An item is over
    /// once you're back, at its footprint's end, or at its own end when it has no footprint. Null when everything
    /// listed is over.
    /// </summary>
    public static NextUp? Next(IReadOnlyList<AgendaItem> items, Instant now)
    {
        var next = items
            .Select(item => (Item: item, Time: item.Footprint?.Whole ?? item.Occurrence.Time))
            .Where(entry => entry.Time.End > now)
            .OrderBy(entry => entry.Time.Start)
            .ThenBy(entry => entry.Item.Occurrence.Time.Start)
            .ThenBy(entry => entry.Item.Activity.Title, StringComparer.Ordinal)
            .ThenBy(entry => entry.Item.Activity.Id)
            .ThenBy(entry => entry.Item.Occurrence.Date)
            .FirstOrDefault();
        return next.Item is null ? null : new NextUp(next.Item, UnderWay: next.Time.Start <= now);
    }

    /// <summary>
    /// CAL-1: every occurrence on screen in the calendar, that is whose footprint (CAL-5), or the activity itself
    /// without one, overlaps <paramref name="window"/> (half-open). That includes one from the day before running into
    /// it, such as a night shift. Footprints, notes and clashes are as in <see cref="Between"/>, in the same order.
    /// </summary>
    public static IReadOnlyList<AgendaItem> Overlapping(IEnumerable<PlannedActivity> activities, PlanningContext context, Interval window, DateTimeZone zone)
    {
        var (from, through) = DatesAround(window, zone);
        return Between(activities, context, from, through)
            .Where(item => (item.Footprint?.Whole ?? item.Occurrence.Time) is var time && time.Start < window.End && time.End > window.Start)
            .ToList();
    }

    /// <summary>
    /// The dates whose activities <see cref="Overlapping"/> needs, for callers deciding which to load: its own dates
    /// and <see cref="MarginDays"/> either side of them, for clashes.
    /// </summary>
    public static DateInterval DatesToLoadFor(Interval window, DateTimeZone zone)
    {
        var (from, through) = DatesAround(window, zone);
        return new DateInterval(from.PlusDays(-MarginDays), through.PlusDays(MarginDays));
    }

    // Two days before: a footprint of up to a day (H8) can start the day before the window's first date, and an activity
    // in another timezone can be dated a day earlier. Overlap with the window is what decides.
    private static (LocalDate From, LocalDate Through) DatesAround(Interval window, DateTimeZone zone) =>
        (window.Start.InZone(zone).Date.PlusDays(-2), window.End.InZone(zone).Date.PlusDays(1));

    /// <summary>The time it starts, saying which one when the clocks repeat it ("01:30 BST"), the way the user chose (ACC-3).</summary>
    public static string Starts(AgendaItem item, ClockFormat clock = ClockFormat.TwentyFourHour) => Times.Clock(item.Occurrence.Time.Start.InZone(item.Activity.Series.Zone), clock);

    /// <summary>
    /// When to go and when it's over, from the footprint: "Leave home 17:35, back 19:45" away from home (leaving is when
    /// the journey out starts, after getting ready at home, §7.1), "With getting ready and changing: 06:55 to 07:40" for
    /// buffers at home, or null. A time on another day says "the day before" or "the next day" (§8), and a time the
    /// clocks repeat says which one ("01:15 GMT").
    /// </summary>
    public static string? Trip(AgendaItem item, ClockFormat clock = ClockFormat.TwentyFourHour)
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
            return Times.Clock(local, clock) + Times.RelativeTo(local.Date, day);
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
    public static readonly LocalDatePattern Date = DateText.Named;

    /// <summary>H8's limit for an occurrence: 24 hours, or the whole day it starts on if that day is longer (Q7).</summary>
    public static Duration LongestFor(Interval occurrence, DateTimeZone zone) =>
        Footprint.LongestOn(occurrence.Start.InZone(zone).Date, zone);

    /// <summary>
    /// How a moment's date reads next to <paramref name="day"/>, to follow a time: nothing on that day, " the next day"
    /// or " the day before", or " on Tue 27 Oct 2026" further away (§8: never "the next day" for two days later).
    /// </summary>
    public static string RelativeTo(LocalDate date, LocalDate day) =>
        date == day ? ""
        : date == day.PlusDays(1) ? " the next day"
        : date == day.PlusDays(-1) ? " the day before"
        : $" on {Date.Format(date)}";

    /// <summary>"50 min", "8 h" or "1 h 30 min".</summary>
    public static string Length(Duration length)
    {
        var minutes = (long)length.TotalMinutes;
        return (minutes / 60, minutes % 60) switch
        {
            (0, var justMinutes) => $"{justMinutes} min",
            (var hours, 0) => $"{hours} h",
            (var hours, var andMinutes) => $"{hours} h {andMinutes} min",
        };
    }

    /// <summary>
    /// "07:30" (or "7:30am" with the 12-hour clock, ACC-3), or "01:15 BST" for a time the clocks repeat on the night
    /// they go back.
    /// </summary>
    public static string Clock(ZonedDateTime moment, ClockFormat clock = ClockFormat.TwentyFourHour) =>
        moment.Zone.MapLocal(moment.LocalDateTime).Count > 1
            ? $"{ClockText.Format(moment.TimeOfDay, clock)} {moment.GetZoneInterval().Name}"
            : ClockText.Format(moment.TimeOfDay, clock);
}
