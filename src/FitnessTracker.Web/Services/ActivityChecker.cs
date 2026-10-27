using System.Globalization;
using System.Text.RegularExpressions;
using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Domain.Time;
using FitnessTracker.Web.Data;
using NodaTime;

namespace FitnessTracker.Web.Services;

/// <summary>
/// SCH-1: runs the one check (<see cref="ActivityCheck"/>) on an activity about to be saved, against the signed-in
/// user's other activities, locations and travel times (<see cref="PlanningData"/>). Every way of saving an activity
/// calls this; the rules themselves are in the domain.
/// </summary>
public class ActivityChecker(PlanningData planning)
{
    /// <summary>
    /// The check's explanations write times as the user chose (ACC-3), or as <paramref name="clock"/> says, and the rest
    /// rules are the saved ones, or <paramref name="rest"/>: two checks whose findings are compared (before and after a
    /// change) must write times the same way and keep the same rules. With <paramref name="checkFrom"/> (later than
    /// today), it is checked as if that were today: for a change to some dates months ahead (a holiday chosen), whose
    /// clashes the usual 8 weeks from today wouldn't reach.
    /// </summary>
    public async Task<ActivityCheckResult> CheckAsync(Activity candidate, ClockFormat? clock = null, RestRules? rest = null, LocalDate? checkFrom = null)
    {
        var loaded = await planning.LoadAsync();
        var today = checkFrom is { } from && from > loaded.Context.Today ? from : loaded.Context.Today;
        var user = loaded with
        {
            Context = loaded.Context with { Clock = clock ?? loaded.Context.Clock, Rest = rest ?? loaded.Context.Rest, Today = today },
        };

        // Only activities that can fall near the dates checked (for a demanding one, as far ahead as its rest rules are
        // checked and as far either side as the rest between demanding sessions reaches, H5-H7), not every one the user
        // ever had; one that can't be read is left out with a note, not allowed to stop every save. Each in the weeks its
        // routine allows (CAL-9), the candidate too.
        var plannedCandidate = candidate.ToPlannedActivity((await planning.RoutinesAsync()).Calendar);
        var unreadable = new List<string>();
        var nearby = ActivityCheck.OthersThatCanClash(plannedCandidate, user.Context.Today, user.Context.Rest.BetweenDemanding);
        var others = await planning.ActivitiesOnAsync(nearby, unreadable, except: candidate.Id);

        var result = ActivityCheck.Check(plannedCandidate, others, user.Context);
        return unreadable.Count == 0 ? result : result with { Notes = [.. unreadable, .. result.Notes] };
    }

    /// <summary>
    /// Q8, SCH-1: each of <paramref name="activities"/>' problems and clashes as the check on save sees them now, in plain
    /// English, by title ("Gym: On Mon 2 Nov 2026, …"). For changes that move activities without their own form (a
    /// timezone change, a routine chosen for some dates): run before and after, what is new is what the change broke.
    /// Both runs must write times the same way (<paramref name="clock"/>) and keep the same rest rules
    /// (<paramref name="rest"/>, or the saved ones), so a change of either alone doesn't make a finding look new.
    /// <paramref name="checkFrom"/> as for <see cref="CheckAsync"/>.
    /// </summary>
    public async Task<List<string>> FindingsAsync(IEnumerable<Activity> activities, ClockFormat clock, RestRules? rest = null, LocalDate? checkFrom = null)
    {
        var findings = new List<string>();
        foreach (var activity in activities)
        {
            var result = await CheckAsync(activity, clock, rest, checkFrom);
            var zone = DateTimeZoneProviders.Tzdb[activity.TimeZoneId];
            findings.AddRange(result.Problems.Select(problem => $"{activity.Title}: {problem.Message}"));
            findings.AddRange(result.Clashes.Select(clash => $"{activity.Title}: {ActivityCheck.Describe(clash, zone, clock)}"));
        }

        return findings;
    }

    /// <summary>
    /// CAL-9, SCH-1: <see cref="FindingsAsync"/> for every activity a change to which routine is in use when (dates chosen
    /// for a routine, another made usual) can move, run before it; run again after it, what is new is what it broke
    /// (<see cref="RoutineActivityFindingsSinceAsync"/>). Those in a routine, and, while a routine has a base or
    /// <paramref name="givesABase"/> (TRV-4), those in every week at a place too, whose travel can then start elsewhere
    /// (code review, Phase 6b). Checked from today, or, for a change to some dates (<paramref name="changedFrom"/>: the
    /// first day of each range chosen or no longer chosen), from each of those, so a holiday months ahead is checked where
    /// it is (scheduling review, Phase 6b).
    /// </summary>
    public async Task<RoutineFindings> RoutineActivityFindingsAsync(IReadOnlyCollection<LocalDate>? changedFrom = null, bool givesABase = false)
    {
        var everyWeekToo = givesABase || (await planning.LoadAsync()).Context.RoutineBases.Count > 0;
        return new RoutineFindings(await FindingsARoutineChangeCanMoveAsync(changedFrom, everyWeekToo), changedFrom, everyWeekToo);
    }

    /// <summary>
    /// What <see cref="RoutineActivityFindingsAsync"/> found before a change, and how, so the check after it is made the
    /// same way.
    /// </summary>
    public sealed record RoutineFindings(IReadOnlyList<string> Findings, IReadOnlyCollection<LocalDate>? ChangedFrom, bool EveryWeekToo);

    private async Task<List<string>> FindingsARoutineChangeCanMoveAsync(IReadOnlyCollection<LocalDate>? changedFrom, bool everyWeekToo)
    {
        var user = await planning.LoadAsync();
        var clock = user.Preferences.TimesShownAs();
        var activities = await planning.ActivitiesARoutineChangeCanMoveAsync(everyWeekToo, user.Context.Today);
        if (changedFrom is null || changedFrom.Count == 0)
        {
            return await FindingsAsync(activities, clock);
        }

        var findings = new List<string>();
        foreach (var from in changedFrom.Distinct().Order())
        {
            findings.AddRange(await FindingsAsync(activities, clock, checkFrom: from));
        }

        return findings.Distinct().ToList();
    }

    /// <summary>
    /// What the check finds now, made as it was <paramref name="before"/> the change, that it didn't find then, to show.
    /// </summary>
    public async Task<string[]> RoutineActivityFindingsSinceAsync(RoutineFindings before) =>
        NewFindings(await FindingsARoutineChangeCanMoveAsync(before.ChangedFrom, before.EveryWeekToo), before.Findings);

    /// <summary>
    /// The most findings a page is sent after a change. They travel to it in a cookie (TempData), and hundreds (a clash
    /// each day for 8 weeks, for several activities) would be too big for one (code review, Phase 6b).
    /// </summary>
    public const int MostFindingsShown = 20;

    /// <summary>
    /// What a change broke: the findings in <paramref name="after"/> that weren't in <paramref name="before"/>, at most
    /// <see cref="MostFindingsShown"/> of them, then "And 22 more." for the rest. A rule broken on several dates is one
    /// sentence counting the rest ("The same happens on 1 more date up to …"), so the same sentence on fewer dates isn't
    /// new (the change fixed some), and on more dates is (scheduling review, Phase 6b).
    /// </summary>
    public static string[] NewFindings(IReadOnlyList<string> after, IReadOnlyList<string> before)
    {
        var datesBefore = before.Select(Dates).GroupBy(finding => finding.Sentence).ToDictionary(group => group.Key, group => group.Max(finding => finding.Count));
        var added = after.Where(finding => Dates(finding) is var (sentence, count) && (!datesBefore.TryGetValue(sentence, out var was) || count > was)).ToList();
        return added.Count <= MostFindingsShown
            ? [.. added]
            : [.. added.Take(MostFindingsShown), $"And {added.Count - MostFindingsShown} more. The calendar shows each one."];
    }

    // " The same happens on 2 more dates up to Tue 19 Oct 2027." at the end of a rule's sentence (ActivityCheck).
    private static readonly Regex MoreDates = new(@" The same happens on (\d+) more dates? up to [^.]+\.$", RegexOptions.CultureInvariant);

    // A finding without its count of further dates, and how many dates it is on.
    private static (string Sentence, int Count) Dates(string finding) => MoreDates.Match(finding) is { Success: true } more
        ? (finding[..more.Index], 1 + int.Parse(more.Groups[1].Value, CultureInfo.InvariantCulture))
        : (finding, 1);

    /// <summary>
    /// CAL-4: the check for a change to one occurrence of <paramref name="candidate"/> (as it would be saved), on that
    /// occurrence alone, where it now is (<see cref="ActivityCheck.CheckOccurrence"/>). <paramref name="occurrenceDate"/>
    /// is the date its rule puts it on.
    /// </summary>
    public async Task<ActivityCheckResult> CheckOccurrenceAsync(Activity candidate, LocalDate occurrenceDate)
    {
        var user = await planning.LoadAsync();
        var plannedCandidate = candidate.ToPlannedActivity((await planning.RoutinesAsync()).Calendar);
        var unreadable = new List<string>();
        var nearby = ActivityCheck.OthersThatCanClashWith(plannedCandidate.Series, occurrenceDate, user.Context.Rest.BetweenDemanding);
        var others = await planning.ActivitiesOnAsync(nearby, unreadable, except: candidate.Id);

        var result = ActivityCheck.CheckOccurrence(plannedCandidate, occurrenceDate, others, user.Context);
        return unreadable.Count == 0 ? result : result with { Notes = [.. unreadable, .. result.Notes] };
    }
}
