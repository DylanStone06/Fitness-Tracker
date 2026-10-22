using FitnessTracker.Domain.Footprints;
using FitnessTracker.Domain.Locations;
using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Domain.Time;
using NodaTime;
using NodaTime.Text;

namespace FitnessTracker.Domain.Scheduling;

/// <summary>One activity as the check sees it: a series (a one-off is a series that happens once), its buffers and place.</summary>
public sealed record PlannedActivity(Guid Id, string Title, Series Series, Buffers Buffers, Guid? LocationId)
{
    /// <summary>GOL-1: the weekly goal it was planned for, whose sessions it counts towards, or null.</summary>
    public Guid? GoalId { get; init; }

    /// <summary>FIT-6: a hard session that needs recovery afterwards (H5-H7).</summary>
    public bool IsDemanding { get; init; }
}

/// <summary>One of the user's locations: its name, for explanations, and its opening hours (H4).</summary>
public sealed record Place(string Name, OpeningHours OpeningHours);

/// <summary>
/// What the check needs besides the activities: today, home (the base for travel, §7.1), travel times and places, and
/// how the user wants times written in its explanations (ACC-3).
/// </summary>
public sealed record PlanningContext(
    LocalDate Today, Guid? Home, TravelTimes Travel, IReadOnlyDictionary<Guid, Place> Places, ClockFormat Clock = ClockFormat.TwentyFourHour)
{
    /// <summary>ACC-7: H5-H7 from the user's preferences; <see cref="RestRules.None"/> checks none.</summary>
    public RestRules Rest { get; init; } = RestRules.None;
}

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
public sealed record ActivityCheckResult(IReadOnlyList<CheckProblem> Problems, IReadOnlyList<Clash> Clashes, DateInterval Checked)
{
    /// <summary>Other activities that could only be checked in part, in plain English. They don't stop the save.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}

/// <summary>
/// SCH-1, first part: the check every new or changed activity goes through before it is saved, whatever made the
/// change. Problems that stop the save: a repeat that never happens, travel that can't be worked out (never guessed
/// as zero, P1), a footprint longer than the day allows (H8), and an activity outside its location's opening hours on any date
/// (H4). Clashes with other activities' footprints (H1) are only looked for once there are no problems, because they
/// depend on the travel and length being right. A repeating activity is checked for <see cref="WeeksAhead"/> weeks
/// from today, or from its first date if that is later: rules such as "every 98 weeks" and "every 99 weeks" can't be
/// proved never to clash in any shorter time than centuries.
/// </summary>
public static class ActivityCheck
{
    public const int WeeksAhead = 8;

    /// <summary>
    /// Opening hours (H4) and the length limit (H8) are checked further ahead than clashes: they can't be overridden,
    /// they are cheap, and they depend on the weekday and on the clocks changing, which a year covers. A daily rule
    /// every N days is checked for at least 7 x N days, so every weekday it can fall on is reached.
    /// </summary>
    public const int WeeksAheadForHoursAndLength = 53;

    // Other activities starting this many days either side of the dates checked can still overlap (Agenda.MarginDays).
    private const int MarginDays = Agenda.MarginDays;

    private static readonly LocalDatePattern Date = Times.Date;


    public static ActivityCheckResult Check(PlannedActivity candidate, IEnumerable<PlannedActivity> others, PlanningContext context)
    {
        var series = candidate.Series;
        var firstChecked = FirstCheckedDate(series, context.Today);
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

        // The usual length against an ordinary day (no limit given: 24 hours). A length of zero or less can't even be
        // turned into occurrences, so it is the only thing left to say.
        (Duration Length, Duration? Longest) usual = (series.Length, null);
        if (series.Length <= Duration.Zero)
        {
            problems.AddRange(LengthProblems([usual], candidate.Buffers, travel));
            return new ActivityCheckResult(problems, [], dates);
        }

        var everyDaysOfTheWeek = series.Rule.Frequency == RepeatFrequency.Daily ? series.Rule.Every * 7 : 0;
        var longerDates = new DateInterval(firstChecked, firstChecked.PlusDays(Math.Max(WeeksAheadForHoursAndLength * 7, everyDaysOfTheWeek) - 1));
        // CAL-4, SCH-1: an occurrence moved somewhere else is checked wherever it lands, as well as these dates; one moved
        // into the past only when the whole series is (MovedOutside).
        // In time order, so explanations name the earliest first, and up to the last date checked.
        var longerOccurrences = series.OccurrencesBetween(longerDates.Start, longerDates.End)
            .Concat(MovedOutside(series, longerDates, context.Today))
            .OrderBy(occurrence => occurrence.Time.Start)
            .ToList();
        var checkedUpTo = longerOccurrences.Count == 0
            ? longerDates.End
            : LocalDate.Max(longerDates.End, longerOccurrences[^1].Time.Start.InZone(series.Zone).Date);

        // H8 on every occurrence, each against its own day: a moved one can be longer, and the whole 25-hour day the
        // clocks go back is allowed (Q7). The usual length is also checked against an ordinary day whenever the rule
        // still falls on dates after these, which can't all be checked one by one (a 25-hour Sunday every 60 weeks
        // lands on an ordinary Sunday next time). When every date left is inside them, each has been checked already.
        var lengths = longerOccurrences.Select(occurrence => (occurrence.Time.Duration, (Duration?)Times.LongestFor(occurrence.Time, series.Zone)));
        problems.AddRange(LengthProblems(FallsAfter(series, longerDates.End) ? lengths.Prepend(usual) : lengths, candidate.Buffers, travel));
        if (OpeningHoursProblem(candidate, longerOccurrences, context, series.Zone, checkedUpTo) is { } outsideOpeningHours)
        {
            problems.Add(outsideOpeningHours);
        }

        // H5-H7 (ACC-7) on every occurrence in the clash window, against the demanding sessions near them.
        var occurrences = series.OccurrencesBetween(dates.Start, dates.End)
            .Concat(MovedOutside(series, dates, context.Today))
            .OrderBy(occurrence => occurrence.Time.Start)
            .ToList();
        var restCheckedUpTo = occurrences.Count == 0 ? dates.End : LocalDate.Max(dates.End, occurrences[^1].Time.Start.InZone(series.Zone).Date);
        problems.AddRange(RestProblems(candidate, occurrences, others, context, restCheckedUpTo));

        if (problems.Count > 0 || journeys is not { } knownJourneys)
        {
            return new ActivityCheckResult(problems, [], dates);
        }

        // The activity's own stored version is left out: an edit would otherwise clash with itself.
        var notes = new List<string>();
        var nearby = NearbyDates(series, context.Today);
        var otherBookings = others
            .Where(other => other.Id != candidate.Id)
            .SelectMany(other => BookingsOf(other, nearby, context, notes, series.Zone))
            .ToList();
        var candidateBookings = occurrences
            .Select(occurrence => new Booking(candidate.Title, Footprint.Around(occurrence.Time, candidate.Buffers, knownJourneys, Times.LongestFor(occurrence.Time, series.Zone))))
            .ToList();

        // H1 also within the activity itself: a very long one can run into its own next occurrence on the night the
        // clocks go forward. The home page shows that, so the check reports it too.
        var clashes = candidateBookings
            .SelectMany(booking => Conflicts.Between(booking, otherBookings))
            .Concat(Conflicts.Among(candidateBookings))
            .OrderBy(clash => clash.Overlap.Start)
            .ToList();
        return new ActivityCheckResult(problems, clashes, dates) { Notes = notes };
    }

    /// <summary>
    /// SCH-1, CAL-4: the check for a change to one occurrence (moved, resized or put back), on that occurrence alone,
    /// wherever it now is: its travel (never guessed, P1), its length against its own day (H8), the opening hours (H4),
    /// and clashes (H1) with the other activities and with the rest of its own series. A cancelled one takes no time,
    /// so it has nothing to check. Problems and clashes elsewhere in the series aren't the change's doing, so they
    /// aren't looked at. <paramref name="occurrenceDate"/> is the date the rule puts it on.
    /// </summary>
    public static ActivityCheckResult CheckOccurrence(PlannedActivity candidate, LocalDate occurrenceDate, IEnumerable<PlannedActivity> others, PlanningContext context)
    {
        var series = candidate.Series;
        if (OccurrenceOn(series, occurrenceDate) is not { } occurrence)
        {
            return new ActivityCheckResult([], [], new DateInterval(occurrenceDate, occurrenceDate));
        }

        var day = occurrence.Time.Start.InZone(series.Zone).Date;
        var dates = new DateInterval(day, day);
        var problems = new List<CheckProblem>();
        var journeys = JourneysFor(candidate, context, problems);
        var longest = Times.LongestFor(occurrence.Time, series.Zone);
        problems.AddRange(LengthProblems([(occurrence.Time.Duration, longest)], candidate.Buffers, journeys ?? Journeys.None));
        if (OpeningHoursProblem(candidate, [occurrence], context, series.Zone, day) is { } outsideOpeningHours)
        {
            problems.Add(outsideOpeningHours);
        }

        problems.AddRange(RestProblems(candidate, [occurrence], others, context, day));
        if (problems.Count > 0 || journeys is not { } knownJourneys)
        {
            return new ActivityCheckResult(problems, [], dates);
        }

        // Its own series is one of the activities it can run into: the others, under its own title.
        var notes = new List<string>();
        IReadOnlyList<DateInterval> nearby = [Around(day)];
        var rest = candidate with { Series = series.CancelOccurrence(occurrenceDate) };
        var bookings = others
            .Where(other => other.Id != candidate.Id)
            .Append(rest)
            .SelectMany(other => BookingsOf(other, nearby, context, notes, series.Zone))
            .ToList();
        var booking = new Booking(candidate.Title, Footprint.Around(occurrence.Time, candidate.Buffers, knownJourneys, longest));
        var clashes = Conflicts.Between(booking, bookings).OrderBy(clash => clash.Overlap.Start).ToList();
        return new ActivityCheckResult(problems, clashes, dates) { Notes = notes };
    }

    /// <summary>
    /// The dates on which another activity has to start (or be running) to be able to clash with the occurrence the
    /// rule puts on <paramref name="occurrenceDate"/>, where it now is: 3 days either side, or further when the rest
    /// between demanding sessions (H5) reaches further. For <see cref="CheckOccurrence"/>.
    /// </summary>
    public static DateInterval OthersThatCanClashWith(Series series, LocalDate occurrenceDate, Duration betweenDemanding = default) =>
        Wider(Around(OccurrenceOn(series, occurrenceDate)?.Time.Start.InZone(series.Zone).Date ?? occurrenceDate), betweenDemanding);

    /// <summary>
    /// H5: how many days either side a demanding session's rest reaches, for loading the sessions it must be kept apart
    /// from (a day more than the hours, rounded up, so a session late the day before is included). None for no rest.
    /// </summary>
    public static int RestReachDays(Duration betweenDemanding) =>
        betweenDemanding <= Duration.Zero ? 0 : (int)Math.Ceiling(betweenDemanding.TotalDays) + 1;

    // The dates, with more days either side when the rest between demanding sessions reaches past MarginDays.
    private static DateInterval Wider(DateInterval dates, Duration betweenDemanding)
    {
        var extra = Math.Max(0, RestReachDays(betweenDemanding) - MarginDays);
        return new DateInterval(dates.Start.PlusDays(-extra), dates.End.PlusDays(extra));
    }

    // H5-H7 (ACC-7): for a demanding activity, each checked occurrence against the demanding sessions near it, other
    // activities' and its own, start to start in elapsed time (§8). Each rule is said once, naming the first occurrence
    // that breaks it and counting the rest. Validation errors (§7.2), like H4 and H8, not clashes to accept.
    private static IEnumerable<CheckProblem> RestProblems(
        PlannedActivity candidate, IReadOnlyList<Occurrence> occurrences, IEnumerable<PlannedActivity> others, PlanningContext context, LocalDate checkedUpTo)
    {
        var rest = context.Rest;
        if (!candidate.IsDemanding || occurrences.Count == 0)
        {
            return [];
        }

        var zone = candidate.Series.Zone;
        var reach = Math.Max(1, RestReachDays(rest.BetweenDemanding));
        var from = occurrences.Min(occurrence => occurrence.Time.Start).InZone(zone).Date.PlusDays(-reach);
        var through = occurrences.Max(occurrence => occurrence.Time.Start).InZone(zone).Date.PlusDays(reach);
        var demanding = others
            .Where(other => other.IsDemanding && other.Id != candidate.Id)
            .SelectMany(other => other.Series.OccurrencesBetween(from, through).Select(occurrence => (other.Title, Occurrence: occurrence)))
            .Concat(candidate.Series.OccurrencesBetween(from, through).Select(occurrence => (candidate.Title, Occurrence: occurrence)))
            .ToList();

        var tooClose = new List<(Occurrence Mine, string Title, Instant Other)>();
        var crowded = new List<(LocalDate Date, List<string> Titles)>();
        var recovery = new List<LocalDate>();
        foreach (var mine in occurrences.OrderBy(occurrence => occurrence.Time.Start))
        {
            var start = mine.Time.Start;
            var date = start.InZone(zone).Date;
            var near = demanding.Where(other => other.Occurrence != mine).ToList();
            if (near.Where(other => Abs(other.Occurrence.Time.Start - start) < rest.BetweenDemanding)
                    .OrderBy(other => Abs(other.Occurrence.Time.Start - start))
                    .ThenBy(other => other.Occurrence.Time.Start)
                    .Select(other => (other.Title, (Instant?)other.Occurrence.Time.Start))
                    .FirstOrDefault() is (var title, { } closest))
            {
                tooClose.Add((mine, title, closest));
            }

            var sameDay = near.Where(other => other.Occurrence.Time.Start.InZone(zone).Date == date).Select(other => other.Title).ToList();
            if (sameDay.Count >= rest.MostDemandingPerDay)
            {
                crowded.Add((date, sameDay));
            }

            if (rest.RecoveryDays.Contains(date.DayOfWeek))
            {
                recovery.Add(date);
            }
        }

        var problems = new List<CheckProblem>();
        if (tooClose.Count > 0)
        {
            var (mine, title, other) = tooClose[0];
            var at = other.InZone(zone);
            var hours = (int)rest.BetweenDemanding.TotalHours;
            problems.Add(new CheckProblem(CheckAspect.Time,
                $"It's {Times.Length(Abs(other - mine.Time.Start))} from {title}, a demanding session on {Date.Format(at.Date)} at {Times.Clock(at, context.Clock)}; " +
                $"your preferences keep demanding sessions at least {hours} {(hours == 1 ? "hour" : "hours")} apart.{More(tooClose.Count, checkedUpTo)}"));
        }

        if (crowded.Count > 0)
        {
            var (date, titles) = crowded[0];
            var already = titles.Count == 1 ? $"there's already a demanding session, {titles[0]}" : $"there are already {titles.Count} demanding sessions";
            problems.Add(new CheckProblem(CheckAspect.Time,
                $"On {Date.Format(date)} {already}; your preferences allow {rest.MostDemandingPerDay} a day.{More(crowded.Count, checkedUpTo)}"));
        }

        if (recovery.Count > 0)
        {
            problems.Add(new CheckProblem(CheckAspect.Time,
                $"{Date.Format(recovery[0])} is one of your recovery days, and this is a demanding session.{More(recovery.Count, checkedUpTo)}"));
        }

        return problems;
    }

    // " The same happens on 7 more dates up to Tue 29 Dec 2026." after the first of several, or nothing.
    private static string More(int count, LocalDate checkedUpTo) =>
        count > 1 ? $" The same happens on {count - 1} more {(count == 2 ? "date" : "dates")} up to {Date.Format(checkedUpTo)}." : "";

    private static Duration Abs(Duration duration) => duration < Duration.Zero ? -duration : duration;

    // The occurrence the rule puts on date, where it now is, or null when it is cancelled or the rule doesn't fall then.
    private static Occurrence? OccurrenceOn(Series series, LocalDate date)
    {
        var lands = series.Changes.TryGetValue(date, out var change) && change is OccurrenceChange.Moved move ? move.Start.Date : date;
        return series.OccurrencesBetween(lands.PlusDays(-1), lands.PlusDays(1))
            .Select(occurrence => (Occurrence?)occurrence)
            .FirstOrDefault(occurrence => occurrence!.Value.Date == date);
    }

    private static DateInterval Around(LocalDate day) => new(day.PlusDays(-MarginDays), day.PlusDays(MarginDays));

    /// <summary>
    /// The dates on which another activity has to start (or, for a repeating one, be running) to be able to clash with
    /// <paramref name="series"/>: the clash window, and any occurrence moved outside it that is checked (not one moved
    /// into the past), 3 days either side. Lets a caller load only those activities.
    /// </summary>
    public static DateInterval OthersThatCanClash(Series series, LocalDate today, Duration betweenDemanding = default)
    {
        var nearby = NearbyDates(series, today);
        return Wider(new DateInterval(nearby.Min(dates => dates.Start), nearby.Max(dates => dates.End)), betweenDemanding);
    }

    // The clash window with MarginDays either side, and the same around each occurrence moved outside it.
    private static List<DateInterval> NearbyDates(Series series, LocalDate today)
    {
        var firstChecked = FirstCheckedDate(series, today);
        var window = new DateInterval(firstChecked, firstChecked.PlusWeeks(WeeksAhead).PlusDays(-1));
        return MovedOutside(series, window, today)
            .Select(occurrence => occurrence.Time.Start.InZone(series.Zone).Date)
            .Select(date => new DateInterval(date.PlusDays(-MarginDays), date.PlusDays(MarginDays)))
            .Prepend(new DateInterval(window.Start.PlusDays(-MarginDays), window.End.PlusDays(MarginDays)))
            .ToList();
    }

    // CAL-4: occurrences moved to a date outside dates (which start on the first date checked), which those dates alone
    // would miss (OccurrencesBetween finds a moved one by the date it now starts). One moved to before today is history,
    // not re-checked, like the rest of the series' past, unless it is still running today (a night shift moved to
    // yesterday evening can run into this morning's); but a series with nothing from today on is checked from its own
    // first date (FirstCheckedDate), and so are all its moved ones.
    private static IEnumerable<Occurrence> MovedOutside(Series series, DateInterval dates, LocalDate today)
    {
        var from = dates.Start < today ? Instant.MinValue : today.AtStartOfDayInZone(series.Zone).ToInstant();
        return series.Changes
            .Where(change => change.Value is OccurrenceChange.Moved move && !dates.Contains(move.Start.Date))
            .Select(change => OccurrenceOn(series, change.Key))
            .OfType<Occurrence>()
            .Where(occurrence => occurrence.Time.End > from);
    }

    // Today, or the first date if later: a repeating activity's history isn't re-checked. But one with nothing from
    // today on (a one-off in the past, a series that has ended) is checked from its own first date, so nothing is saved
    // unchecked.
    private static LocalDate FirstCheckedDate(Series series, LocalDate today)
    {
        var fromToday = LocalDate.Max(series.FirstDate, today);
        return fromToday > series.FirstDate && !HappensFrom(series, fromToday) ? series.FirstDate : fromToday;
    }

    // Whether any of the series is on date or later: in the year H4 and H8 look at, by its rule after that (a rule every
    // 60 weeks can skip the whole year), or moved there.
    private static bool HappensFrom(Series series, LocalDate date)
    {
        var yearEnd = date.PlusWeeks(WeeksAheadForHoursAndLength);
        return series.OccurrencesBetween(date, yearEnd).Count > 0
            || FallsAfter(series, yearEnd)
            || series.Changes.Values.Any(change => change is OccurrenceChange.Moved move && move.Start.Date >= date);
    }

    /// <summary>
    /// A clash in plain English, for example "On Mon 2 Nov 2026, its journey back overlaps Dinner (Dinner is 19:30 to
    /// 20:30)." Times are in <paramref name="zone"/>, the checked activity's.
    /// </summary>
    public static string Describe(Clash clash, DateTimeZone zone, ClockFormat clock = ClockFormat.TwentyFourHour)
    {
        var date = clash.Candidate.Footprint.Activity.Start.InZone(zone).Date;
        var other = clash.Other.Label;
        return $"On {Date.Format(date)}, {CandidatePart(clash.CandidatePart)} overlaps {OtherPart(clash.OtherPart, other)} " +
               $"({other} is {Span(clash.Other.Footprint.Activity, zone, onDate: date, clock)}).";
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

    // Whether the series' rule falls on any date after `after`. Without an end it always does; with an end date, if one
    // is left; with a count, if fewer than that many fall up to `after` (counted from the first date).
    private static bool FallsAfter(Series series, LocalDate after)
    {
        var rule = series.Rule;
        if (rule.LastDate is { } lastDate)
        {
            return lastDate > after && rule.Dates(series.FirstDate, from: after.PlusDays(1), through: lastDate).Any();
        }

        return rule.Count is not { } count || rule.Dates(series.FirstDate, from: series.FirstDate, through: after).Count() < count;
    }

    // Each H8 problem once, for whichever lengths break their day's limit (none given: an ordinary day's 24 hours).
    private static IEnumerable<CheckProblem> LengthProblems(IEnumerable<(Duration Length, Duration? Longest)> lengths, Buffers buffers, Journeys journeys)
    {
        var found = lengths.Distinct()
            .SelectMany(length => Footprint.ProblemsWith(length.Length, buffers, journeys, length.Longest).Select(problem => (problem, length.Length)))
            .ToList();
        return found
            .Select(item => item.problem)
            .Distinct()
            .Order()
            .Select(problem => new CheckProblem(CheckAspect.Length, problem == FootprintProblem.LongerThanADay
                ? TooLong(found.Where(item => item.problem == problem).Max(item => item.Length), buffers, journeys)
                : Footprint.Describe(problem)));
    }

    // P1: what makes up the total, so a default filled in for a blank box (ACC-6) or the travel isn't a hidden reason.
    // "With 25 min there, 10 min changing and 45 min back, it would take up 24 h 20 min: more than a day allows ...".
    // The limit isn't given as a number of hours: the long day is 25 hours in London but 24 h 30 min on Lord Howe Island.
    private static string TooLong(Duration length, Buffers buffers, Journeys journeys)
    {
        (Duration Part, string What)[] around =
        [
            (buffers.Preparation, "getting ready"),
            (journeys.Out, "there"),
            (buffers.Changing, "changing"),
            (journeys.Back, "back"),
        ];
        var parts = around.Where(part => part.Part > Duration.Zero).Select(part => $"{Times.Length(part.Part)} {part.What}").ToList();
        var total = around.Aggregate(length, (sum, part) => part.Part > Duration.Zero ? sum + part.Part : sum);
        var list = parts.Count > 1 ? $"{string.Join(", ", parts[..^1])} and {parts[^1]}" : parts.SingleOrDefault();
        var takesUp = $"it would take up {Times.Length(total)}: more than a day allows (24 hours, or all of a longer day, such as the day the clocks go back).";
        return list is null ? char.ToUpperInvariant(takesUp[0]) + takesUp[1..] : $"With {list}, {takesUp}";
    }

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
            ? $"{place.Name} is open {ClockText.Format(hours.Opens, context.Clock)} to {ClockText.Format(hours.Closes, context.Clock)}{(hours.Closes <= hours.Opens ? " the next day" : "")} on {Date.Format(problem.Date)}"
            : $"{place.Name} is closed on {Date.Format(problem.Date)}";
        var more = outside.Count > 1
            ? $" The same happens on {outside.Count - 1} more {(outside.Count == 2 ? "date" : "dates")} up to {Date.Format(checkedUpTo)}."
            : "";
        return new CheckProblem(CheckAspect.Time, $"{when}, but it would be there {Span(time, zone, onDate: problem.Date, context.Clock)}.{more}");
    }

    // Another activity's occurrences on any of the dates as bookings, each once. Stored activities can't stop this
    // check, but what can't be worked out is said in a note, never assumed (P1): travel that can't be worked out (a
    // travel time deleted since) is left out, and an occurrence whose footprint breaks H8 is skipped.
    private static List<Booking> BookingsOf(PlannedActivity other, IReadOnlyList<DateInterval> nearby, PlanningContext context, List<string> notes, DateTimeZone zone)
    {
        var known = Travel.KnownJourneys(other.LocationId, context);
        if (known is null)
        {
            notes.Add($"{other.Title} was checked without its travel, because {Travel.WhyUnknown(other.LocationId, context)}.");
        }

        var journeys = known ?? Journeys.None;
        var bookings = new List<Booking>();
        foreach (var occurrence in nearby.SelectMany(dates => other.Series.OccurrencesBetween(dates.Start, dates.End)).Distinct())
        {
            var longest = Times.LongestFor(occurrence.Time, other.Series.Zone);
            if (Footprint.ProblemsWith(occurrence.Time.Duration, other.Buffers, journeys, longest).Count > 0)
            {
                notes.Add($"{other.Title} on {Date.Format(occurrence.Time.Start.InZone(zone).Date)} wasn't checked: counting getting ready, " +
                          "travel and changing, it would take up more than a day allows.");
                continue;
            }

            bookings.Add(new Booking(other.Title, Footprint.Around(occurrence.Time, other.Buffers, journeys, longest)));
        }

        return bookings;
    }

    private static string NameOf(Guid location, PlanningContext context) => Travel.NameOf(location, context);

    // "19:30 to 20:30", or "22:00 to 06:00 the next day". The start's own date is given when it isn't the date the
    // message is about (an overnight activity from the day before), and a time the clocks repeat says which one it
    // is ("01:15 BST to 01:15 GMT" on the night they go back).
    private static string Span(Interval time, DateTimeZone zone, LocalDate onDate, ClockFormat clock)
    {
        var start = time.Start.InZone(zone);
        var end = time.End.InZone(zone);
        var startDate = start.Date == onDate ? "" : $"on {Date.Format(start.Date)} ";
        return $"{startDate}{Times.Clock(start, clock)} to {Times.Clock(end, clock)}{Times.RelativeTo(end.Date, start.Date)}";
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
