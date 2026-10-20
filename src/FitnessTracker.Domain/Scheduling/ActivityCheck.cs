using FitnessTracker.Domain.Footprints;
using FitnessTracker.Domain.Locations;
using FitnessTracker.Domain.Recurrence;
using NodaTime;
using NodaTime.Text;

namespace FitnessTracker.Domain.Scheduling;

/// <summary>One activity as the check sees it: a series (a one-off is a series that happens once), its buffers and place.</summary>
public sealed record PlannedActivity(Guid Id, string Title, Series Series, Buffers Buffers, Guid? LocationId);

/// <summary>One of the user's locations: its name, for explanations, and its opening hours (H4).</summary>
public sealed record Place(string Name, OpeningHours OpeningHours);

/// <summary>What the check needs besides the activities: today, home (the base for travel, §7.1), travel times and places.</summary>
public sealed record PlanningContext(LocalDate Today, Guid? Home, TravelTimes Travel, IReadOnlyDictionary<Guid, Place> Places);

/// <summary>What a problem is about, so a form can show it next to the right field.</summary>
public enum CheckAspect
{
    Location,
    Time,
    Length,
    Repeat,
}

/// <summary>A problem that stops an activity being saved, in plain English.</summary>
public sealed record CheckProblem(CheckAspect Aspect, string Message);

/// <summary>
/// The result of checking an activity. <see cref="Problems"/> stop the save. <see cref="Clashes"/> (H1) can be saved
/// once the user confirms (requirements §7.2, Q2). <see cref="Checked"/> is the dates looked at.
/// </summary>
public sealed record ActivityCheckResult(IReadOnlyList<CheckProblem> Problems, IReadOnlyList<Clash> Clashes, DateInterval Checked);

/// <summary>
/// SCH-1, first part: the check every new or changed activity goes through before it is saved, whatever made the
/// change. Problems that stop the save: a repeat that never happens, travel that can't be worked out (never guessed
/// as zero, P1), a footprint over 24 hours (H8), and an activity outside its location's opening hours on any date
/// (H4). Clashes with other activities' footprints (H1) are only looked for once there are no problems, because they
/// depend on the travel and length being right. A repeating activity is checked for <see cref="WeeksAhead"/> weeks
/// from today, or from its first date if that is later: rules such as "every 98 weeks" and "every 99 weeks" can't be
/// proved never to clash in any shorter time than centuries.
/// </summary>
public static class ActivityCheck
{
    public const int WeeksAhead = 8;

    /// <summary>
    /// Opening hours (H4) and the 24-hour limit (H8) are checked further ahead than clashes: they can't be overridden,
    /// they are cheap, and they depend on the weekday and on the clocks changing, which a year covers. A daily rule
    /// every N days is checked for at least 7 x N days, so every weekday it can fall on is reached.
    /// </summary>
    public const int WeeksAheadForHoursAndLength = 53;

    // Other activities starting this many days either side of the dates checked can still overlap. Two footprints of
    // up to 24 hours each (H8) can reach 48 hours apart, which spans a third date across a 23-hour day.
    private const int MarginDays = 3;

    private static readonly LocalDatePattern Date = LocalDatePattern.CreateWithInvariantCulture("ddd d MMM uuuu");

    private static readonly LocalTimePattern Time = LocalTimePattern.CreateWithInvariantCulture("HH:mm");

    public static ActivityCheckResult Check(PlannedActivity candidate, IEnumerable<PlannedActivity> others, PlanningContext context)
    {
        var series = candidate.Series;
        var firstChecked = LocalDate.Max(series.FirstDate, context.Today);
        var dates = new DateInterval(firstChecked, firstChecked.PlusWeeks(WeeksAhead).PlusDays(-1));

        if (!series.Rule.FallsOnAnyDate(series.FirstDate))
        {
            return new ActivityCheckResult(
                [new CheckProblem(CheckAspect.Repeat, "It would never happen: the last date is before the first day it repeats on. Choose a later last date, or other days.")],
                [],
                dates);
        }

        var problems = new List<CheckProblem>();
        var journeys = JourneysFor(candidate, context, problems);
        var travel = journeys ?? Journeys.None;

        // A length of zero or less can't even be turned into occurrences, so it is the only thing left to say.
        if (series.Length <= Duration.Zero)
        {
            problems.AddRange(LengthProblems([series.Length], candidate.Buffers, travel));
            return new ActivityCheckResult(problems, [], dates);
        }

        var everyDaysOfTheWeek = series.Rule.Frequency == RepeatFrequency.Daily ? series.Rule.Every * 7 : 0;
        var longerDates = new DateInterval(firstChecked, firstChecked.PlusDays(Math.Max(WeeksAheadForHoursAndLength * 7, everyDaysOfTheWeek) - 1));
        var longerOccurrences = series.OccurrencesBetween(longerDates.Start, longerDates.End);

        // H8 on every occurrence, not just the usual length: a moved one can be longer.
        problems.AddRange(LengthProblems(longerOccurrences.Select(occurrence => occurrence.Time.Duration).Prepend(series.Length), candidate.Buffers, travel));
        if (OpeningHoursProblem(candidate, longerOccurrences, context, series.Zone, longerDates.End) is { } outsideOpeningHours)
        {
            problems.Add(outsideOpeningHours);
        }

        if (problems.Count > 0 || journeys is not { } knownJourneys)
        {
            return new ActivityCheckResult(problems, [], dates);
        }

        var occurrences = series.OccurrencesBetween(dates.Start, dates.End);

        // The activity's own stored version is left out: an edit would otherwise clash with itself.
        var otherBookings = others
            .Where(other => other.Id != candidate.Id)
            .SelectMany(other => BookingsOf(other, dates.Start.PlusDays(-MarginDays), dates.End.PlusDays(MarginDays), context))
            .ToList();
        var clashes = occurrences
            .Select(occurrence => new Booking(candidate.Title, Footprint.Around(occurrence.Time, candidate.Buffers, knownJourneys)))
            .SelectMany(booking => Conflicts.Between(booking, otherBookings))
            .ToList();
        return new ActivityCheckResult(problems, clashes, dates);
    }

    /// <summary>
    /// A clash in plain English, for example "On Mon 2 Nov 2026, its journey back overlaps Dinner (Dinner is 19:30 to
    /// 20:30)." Times are in <paramref name="zone"/>, the checked activity's.
    /// </summary>
    public static string Describe(Clash clash, DateTimeZone zone)
    {
        var date = clash.Candidate.Footprint.Activity.Start.InZone(zone).Date;
        var other = clash.Other.Label;
        return $"On {Date.Format(date)}, {CandidatePart(clash.CandidatePart)} overlaps {OtherPart(clash.OtherPart, other)} " +
               $"({other} is {Span(clash.Other.Footprint.Activity, zone)}).";
    }

    // Null, with a problem added, when travel can't be worked out. None at home or at no particular place (§7.1).
    private static Journeys? JourneysFor(PlannedActivity activity, PlanningContext context, List<CheckProblem> problems)
    {
        if (activity.LocationId is not { } at || at == context.Home)
        {
            return Journeys.None;
        }

        if (context.Home is not { } home)
        {
            problems.Add(new CheckProblem(CheckAspect.Location,
                $"Travel to {NameOf(at, context)} can't be worked out, because none of your locations is marked as home. " +
                "Mark your home on the Locations page, or choose \"Nowhere in particular\"."));
            return null;
        }

        var lookup = context.Travel.JourneysFor(at, home);
        if (lookup.Journeys is { } journeys)
        {
            return journeys;
        }

        var missing = string.Join(", or ", lookup.MissingLegs.Select(leg => $"from {NameOf(leg.From, context)} to {NameOf(leg.To, context)}"));
        problems.Add(new CheckProblem(CheckAspect.Location,
            $"There's no travel time {missing}. Add {(lookup.MissingLegs.Count == 1 ? "it" : "them")} on the Travel times page, " +
            "or choose \"Nowhere in particular\"."));
        return null;
    }

    // Each H8 problem once, for whichever lengths break it.
    private static IEnumerable<CheckProblem> LengthProblems(IEnumerable<Duration> lengths, Buffers buffers, Journeys journeys) =>
        lengths.Distinct()
            .SelectMany(length => Footprint.ProblemsWith(length, buffers, journeys))
            .Distinct()
            .Order()
            .Select(problem => new CheckProblem(CheckAspect.Length, Footprint.Describe(problem)));

    // H4 on every date checked, because opening hours depend on the day. Reports the first date and counts the rest.
    private static CheckProblem? OpeningHoursProblem(PlannedActivity activity, IReadOnlyList<Occurrence> occurrences, PlanningContext context, DateTimeZone zone, LocalDate checkedUpTo)
    {
        if (activity.LocationId is not { } at || !context.Places.TryGetValue(at, out var place))
        {
            return null;
        }

        var outside = occurrences
            .Select(occurrence => (occurrence.Time, Problem: place.OpeningHours.ProblemWith(occurrence.Time, zone)))
            .Where(found => found.Problem is not null)
            .ToList();
        if (outside.Count == 0)
        {
            return null;
        }

        var (time, problem) = outside[0];
        var when = problem!.Hours is { } hours
            ? $"{place.Name} is open {Time.Format(hours.Opens)} to {Time.Format(hours.Closes)}{(hours.Closes <= hours.Opens ? " the next day" : "")} on {Date.Format(problem.Date)}"
            : $"{place.Name} is closed on {Date.Format(problem.Date)}";
        var more = outside.Count > 1
            ? $" The same happens on {outside.Count - 1} more {(outside.Count == 2 ? "date" : "dates")} up to {Date.Format(checkedUpTo)}."
            : "";
        return new CheckProblem(CheckAspect.Time, $"{when}, but it would be there {Span(time, zone)}.{more}");
    }

    // Another activity's occurrences as bookings. Stored activities can't stop this check: travel that can't be worked
    // out (a travel time deleted since) is left out, and an occurrence whose footprint breaks H8 is skipped.
    private static IEnumerable<Booking> BookingsOf(PlannedActivity other, LocalDate from, LocalDate through, PlanningContext context)
    {
        var journeys = other.LocationId is { } at && context.Home is { } home
            ? context.Travel.JourneysFor(at, home).Journeys ?? Journeys.None
            : Journeys.None;
        return other.Series.OccurrencesBetween(from, through)
            .Where(occurrence => Footprint.ProblemsWith(occurrence.Time.Duration, other.Buffers, journeys).Count == 0)
            .Select(occurrence => new Booking(other.Title, Footprint.Around(occurrence.Time, other.Buffers, journeys)));
    }

    private static string NameOf(Guid location, PlanningContext context) =>
        context.Places.TryGetValue(location, out var place) ? place.Name : "that location";

    // "19:30 to 20:30", or "22:00 to 06:00 the next day".
    private static string Span(Interval time, DateTimeZone zone)
    {
        var start = time.Start.InZone(zone);
        var end = time.End.InZone(zone);
        return $"{Time.Format(start.TimeOfDay)} to {Time.Format(end.TimeOfDay)}{(end.Date > start.Date ? " the next day" : "")}";
    }

    private static string CandidatePart(FootprintPart part) => part switch
    {
        FootprintPart.Preparation => "its getting-ready time",
        FootprintPart.TravelOut => "its journey there",
        FootprintPart.Activity => "it",
        FootprintPart.Changing => "its changing time",
        _ => "its journey back",
    };

    private static string OtherPart(FootprintPart part, string other) => part switch
    {
        FootprintPart.Preparation => $"the getting-ready time before {other}",
        FootprintPart.TravelOut => $"the journey to {other}",
        FootprintPart.Activity => other,
        FootprintPart.Changing => $"the changing time after {other}",
        _ => $"the journey back from {other}",
    };
}
