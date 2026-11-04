using FitnessTracker.Domain.Footprints;
using FitnessTracker.Domain.Time;
using NodaTime;

namespace FitnessTracker.Domain.Scheduling;

/// <summary>Something that takes up time, such as one occurrence of an activity, with its footprint. The label is for explanations.</summary>
public sealed record Booking(string Label, Footprint Footprint);

/// <summary>
/// Two bookings whose footprints overlap (H1). The parts named are the ones where the overlap begins, so an
/// explanation can say what really runs into what: "changing time after Gym overlaps Dinner" (T10).
/// <see cref="ActivitiesOverlap"/> says whether the activities themselves overlap too, so an explanation doesn't
/// suggest that shorter travel or buffers would fix a clash they wouldn't.
/// </summary>
public sealed record Clash(Booking Candidate, FootprintPart CandidatePart, Booking Other, FootprintPart OtherPart, Interval Overlap)
{
    public bool ActivitiesOverlap =>
        Candidate.Footprint.Activity.Start < Other.Footprint.Activity.End
        && Other.Footprint.Activity.Start < Candidate.Footprint.Activity.End;
}

/// <summary>
/// H1: footprints don't overlap. Checked on whole footprints, not on the bare activities (glossary), with half-open
/// intervals: one ending at 10:00 and another starting at 10:00 don't conflict (T11). Results don't depend on the
/// order bookings are passed in, even when times tie (SCH-7).
/// </summary>
public static class Conflicts
{
    /// <summary>
    /// The bookings a new or changed booking clashes with, in order of where each overlap begins, then of when the
    /// other booking starts and ends, then by label. The caller passes only the other bookings: a booking compared
    /// with its own stored version would clash with itself.
    /// </summary>
    public static IReadOnlyList<Clash> Between(Booking candidate, IEnumerable<Booking> others) =>
        InTimeOrder(others)
            .Select(other => ClashBetween(candidate, other))
            .OfType<Clash>()
            .OrderBy(clash => clash.Overlap.Start)
            .ToList();

    /// <summary>
    /// Every clashing pair among a set of bookings, each pair once, in order of where the overlap begins (CAL-6). In
    /// each pair, the booking whose footprint starts first is the candidate (if they start together, the one that
    /// ends first, then by label).
    /// </summary>
    public static IReadOnlyList<Clash> Among(IEnumerable<Booking> bookings)
    {
        var ordered = InTimeOrder(bookings).ToList();
        var clashes = new List<Clash>();
        for (var first = 0; first < ordered.Count; first++)
        {
            // Sorted by start, so once a later booking starts at or after this one ends, none after it can overlap.
            for (var second = first + 1;
                 second < ordered.Count && ordered[second].Footprint.Whole.Start < ordered[first].Footprint.Whole.End;
                 second++)
            {
                if (ClashBetween(ordered[first], ordered[second]) is { } clash)
                {
                    clashes.Add(clash);
                }
            }
        }

        return clashes.OrderBy(clash => clash.Overlap.Start).ToList();
    }

    // A fixed order whatever order the bookings arrive in. OrderBy is stable, so sorting this first and then by
    // overlap start keeps these tie-breaks.
    private static IOrderedEnumerable<Booking> InTimeOrder(IEnumerable<Booking> bookings) =>
        bookings
            .OrderBy(booking => booking.Footprint.Whole.Start)
            .ThenBy(booking => booking.Footprint.Whole.End)
            .ThenBy(booking => booking.Label, StringComparer.Ordinal);

    private static Clash? ClashBetween(Booking candidate, Booking other)
    {
        var start = Instant.Max(candidate.Footprint.Whole.Start, other.Footprint.Whole.Start);
        var end = Instant.Min(candidate.Footprint.Whole.End, other.Footprint.Whole.End);
        if (start >= end)
        {
            return null;
        }

        return new Clash(candidate, PartAt(candidate.Footprint, start), other, PartAt(other.Footprint, start), new Interval(start, end));
    }

    // A footprint's parts follow on from each other with no gaps, so every moment inside it is in exactly one part.
    private static FootprintPart PartAt(Footprint footprint, Instant moment) =>
        footprint.Segments.First(segment => segment.Time.Contains(moment)).Part;
}

/// <summary>
/// Requirements §7.3: a day's load is the total footprint time on it (overlapping footprints, such as an accepted
/// conflict, each count). A footprint crossing midnight counts towards each day for the part on that day (§8),
/// measured midnight to midnight in the given zone. For a day's full load, pass the footprints of occurrences from
/// the day before to the day after: preparation and travel can start before midnight. Days with no load are left out.
/// </summary>
public static class DailyLoad
{
    public static IReadOnlyDictionary<LocalDate, Duration> Of(IEnumerable<Footprint> footprints, DateTimeZone zone)
    {
        var load = new SortedDictionary<LocalDate, Duration>();
        foreach (var share in footprints.SelectMany(footprint => CalendarDays.Split(footprint.Whole, zone)))
        {
            load[share.Day] = load.GetValueOrDefault(share.Day) + share.Length;
        }

        return load;
    }
}
