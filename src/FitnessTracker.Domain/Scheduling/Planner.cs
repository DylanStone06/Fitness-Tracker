using FitnessTracker.Domain.Footprints;
using FitnessTracker.Domain.Locations;
using FitnessTracker.Domain.Time;
using NodaTime;

namespace FitnessTracker.Domain.Scheduling;

/// <summary>§7.3: the part of the day a session is preferred in, by its start time (see <see cref="Planner"/>).</summary>
public enum TimeOfDay
{
    Any,
    Morning,
    Midday,
    Afternoon,
    Evening,
}

/// <summary>
/// GOL-1: a weekly goal as the planner needs it. <see cref="Priority"/>: higher is fitted first. Preferred days and
/// time are soft preferences (§7.3), not rules.
/// </summary>
public sealed record PlanGoal(
    Guid Id,
    string Title,
    int SessionsPerWeek,
    Duration Length,
    Buffers Buffers,
    Guid? LocationId,
    bool IsDemanding,
    int Priority,
    IReadOnlySet<IsoDayOfWeek> PreferredDays,
    TimeOfDay PreferredTime);

/// <summary>SLP-1, §7.4: the usual night, kept free: from winding down before bed to getting going after waking.</summary>
public sealed record SleepWindow(LocalTime Bedtime, LocalTime WakeTime, Duration WindDown, Duration WakeUpRoutine);

/// <summary>
/// ACC-7: H5, demanding sessions at least <see cref="BetweenDemanding"/> apart, start to start; H6, at most
/// <see cref="MostDemandingPerDay"/> in a day; H7, none on <see cref="RecoveryDays"/>.
/// </summary>
public sealed record RestRules(Duration BetweenDemanding, int MostDemandingPerDay, IReadOnlySet<IsoDayOfWeek> RecoveryDays)
{
    /// <summary>No rest rules: nothing apart, any number a day, no recovery days.</summary>
    public static RestRules None { get; } = new(Duration.Zero, int.MaxValue, new HashSet<IsoDayOfWeek>());
}

/// <summary>
/// What "Plan my week" plans: the <paramref name="Week"/>'s dates in <paramref name="Zone"/>, from
/// <paramref name="Now"/> on, around every activity already planned (their footprints) and sleep.
/// </summary>
public sealed record PlanRequest(
    DateInterval Week,
    Instant Now,
    DateTimeZone Zone,
    IReadOnlyList<PlannedActivity> Activities,
    IReadOnlyList<PlanGoal> Goals,
    SleepWindow Sleep,
    RestRules Rest,
    PlanningContext Context)
{
    /// <summary>SCH-4: days the user said no to for a goal, which get none of its sessions.</summary>
    public IReadOnlyCollection<RejectedDay> Rejected { get; init; } = [];
}

/// <summary>SCH-4: a day the user said no to for one goal's session (another time that day: change the suggestion).</summary>
public readonly record struct RejectedDay(Guid Goal, LocalDate Date);

/// <summary>SCH-3: a time for one session of a goal, with what it takes up and why it was chosen (§7.3).</summary>
public sealed record Suggestion(PlanGoal Goal, Interval Time, Footprint Footprint, IReadOnlyList<string> Reasons);

/// <summary>SCH-5: why a day had no time for a session, in plain English with the numbers.</summary>
public sealed record DayReason(LocalDate Date, string Reason);

/// <summary>
/// SCH-5: sessions of a goal that didn't fit, each day's reason (days that are over, or already have as many of its
/// sessions as a day allows, aren't listed), and a shorter session that would fit, if any. <see cref="Blocked"/> says
/// why none could be planned at all (travel that can't be worked out, P1).
/// </summary>
public sealed record Shortfall(PlanGoal Goal, int Missing, IReadOnlyList<DayReason> Days, string? Shorter = null, string? Blocked = null)
{
    /// <summary>Why, when no day is left to give a reason: each day left already has as many sessions as a day allows.</summary>
    public string? Note { get; init; }
}

/// <summary>CAL-7: a day's free time, not counting sleep or anything planned: in all, and the longest stretch.</summary>
public sealed record FreeDay(LocalDate Date, Duration Total, Duration Longest);

/// <summary>SCH-6: other times for one session, best first, or why there are none (<see cref="None"/>).</summary>
public sealed record SessionAlternatives(IReadOnlyList<Suggestion> Options, Shortfall? None);

/// <summary>
/// The suggestions in time order, what didn't fit, how many sessions of each goal were planned already, and
/// <see cref="Notes"/>: what couldn't be counted (an activity's travel that can't be worked out, P1).
/// </summary>
public sealed record WeekPlan(IReadOnlyList<Suggestion> Suggestions, IReadOnlyList<Shortfall> Shortfalls, IReadOnlyDictionary<Guid, int> AlreadyPlanned)
{
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>CAL-7: each day not over, its free time as the calendar is now, before any suggestion.</summary>
    public IReadOnlyList<FreeDay> FreeTime { get; init; } = [];
}

/// <summary>
/// SCH-2, SCH-3, SCH-5, SCH-7, requirements §7.4: plans the sessions weekly goals still need. Busy time is every
/// footprint already planned and each night's sleep window. For each missing session, highest priority first, start
/// times every <see cref="Step"/> are tried; those that keep the hard rules (H1 footprints don't overlap, H4 opening
/// hours, H5-H7 rest, H8 a day's length; never in the past) are scored by <see cref="Scores"/> and the best taken,
/// earliest on a tie. It is greedy: fast, explainable and the same every time, but not guaranteed to find the best
/// possible week. A session counts towards a goal when its activity was planned for it (<see cref="PlannedActivity.GoalId"/>).
/// </summary>
public static class Planner
{
    /// <summary>Requirements Q1: start times are tried every 15 minutes, on the quarter hour.</summary>
    public static readonly Duration Step = Duration.FromMinutes(15);

    /// <summary>
    /// The dates whose activities a plan of <paramref name="week"/> needs: <see cref="Agenda.MarginDays"/> either side for
    /// clashes, or more when the rest between demanding sessions (H5) reaches further.
    /// </summary>
    public static DateInterval DatesToLoadFor(DateInterval week, Duration betweenDemanding)
    {
        var margin = Math.Max(Agenda.MarginDays, ActivityCheck.RestReachDays(betweenDemanding));
        return new DateInterval(week.Start.PlusDays(-margin), week.End.PlusDays(margin));
    }

    /// <summary>Shorter sessions are offered in steps of 5 minutes, down to this.</summary>
    private static readonly Duration ShortestOffered = Duration.FromMinutes(15);

    public static WeekPlan Plan(PlanRequest request)
    {
        var week = new WeekState(request);
        var freeTime = week.FreeTime();
        var suggestions = new List<Suggestion>();
        var shortfalls = new List<Shortfall>();
        var alreadyPlanned = new Dictionary<Guid, int>();
        foreach (var goal in request.Goals.OrderByDescending(goal => goal.Priority).ThenBy(goal => goal.Title, StringComparer.Ordinal).ThenBy(goal => goal.Id))
        {
            var planned = week.SessionDates(goal.Id).Count(request.Week.Contains);
            alreadyPlanned[goal.Id] = planned;
            var missing = Math.Max(0, goal.SessionsPerWeek - planned);
            if (missing == 0)
            {
                continue;
            }

            if (Travel.KnownJourneys(goal.LocationId, request.Context) is not { } journeys)
            {
                shortfalls.Add(new Shortfall(goal, missing, [], Blocked: Sentence(Travel.WhyUnknown(goal.LocationId, request.Context))));
                continue;
            }

            if (Footprint.ProblemsWith(goal.Length, goal.Buffers, journeys).Count > 0)
            {
                shortfalls.Add(new Shortfall(goal, missing, [],
                    Blocked: $"Counting getting ready, travel and changing, each {goal.Title} session would take up more than a day"));
                continue;
            }

            for (var placed = 0; placed < missing; placed++)
            {
                if (week.Best(goal, journeys) is not { } best)
                {
                    var days = week.DayReasons(goal, journeys);
                    shortfalls.Add(new Shortfall(goal, missing - placed, days) { Note = days.Count == 0 ? week.NoDaysLeft(goal) : null });
                    break;
                }

                suggestions.Add(best);
                week.Add(best);
            }
        }

        // A shorter session is only offered once every goal has its times, so it is never at a time one of them was
        // given (scheduling review M-4).
        for (var i = 0; i < shortfalls.Count; i++)
        {
            if (shortfalls[i].Blocked is null && Travel.KnownJourneys(shortfalls[i].Goal.LocationId, request.Context) is { } journeys)
            {
                shortfalls[i] = shortfalls[i] with { Shorter = week.Shorter(shortfalls[i].Goal, journeys, ShortestOffered) };
            }
        }

        return new WeekPlan(
            suggestions.OrderBy(suggestion => suggestion.Time.Start).ThenBy(suggestion => suggestion.Goal.Title, StringComparer.Ordinal).ToList(),
            shortfalls,
            alreadyPlanned)
        {
            Notes = week.Notes,
            FreeTime = freeTime,
        };
    }

    /// <summary>
    /// SCH-6, T16: other times for one session (a missed or skipped one, or one to move) in the request's week, from
    /// <see cref="PlanRequest.Now"/> on: the best on each day, best first, at most <paramref name="most"/>, around
    /// everything else, which stays where it is. Pass the activities without the session itself (its occurrence
    /// cancelled), or it is in its own way, and its time now as <paramref name="notAt"/>: that day's next best is
    /// offered instead. When none fits, <see cref="SessionAlternatives.None"/> says why. For a stored activity's
    /// occurrence use <see cref="OptionsFor"/>, which does all that.
    /// </summary>
    public static SessionAlternatives Options(PlanRequest request, PlanGoal session, int most, Instant? notAt = null)
    {
        var week = new WeekState(request) { NotAt = notAt };
        if (Travel.KnownJourneys(session.LocationId, request.Context) is not { } journeys)
        {
            return new SessionAlternatives([], new Shortfall(session, 1, [], Blocked: Sentence(Travel.WhyUnknown(session.LocationId, request.Context))));
        }

        if (Footprint.ProblemsWith(session.Length, session.Buffers, journeys).Count > 0)
        {
            return new SessionAlternatives([], new Shortfall(session, 1, [],
                Blocked: $"Counting getting ready, travel and changing, {session.Title} would take up more than a day"));
        }

        var options = week.BestEachDay(session, journeys).Take(most).ToList();
        if (options.Count > 0)
        {
            return new SessionAlternatives(options, null);
        }

        var days = week.DayReasons(session, journeys);
        return new SessionAlternatives([], new Shortfall(session, 1, days) { Note = days.Count == 0 ? week.NoDaysLeft(session) : null });
    }

    /// <summary>
    /// SCH-6, T16: <see cref="Options"/> for the occurrence of a stored <paramref name="activity"/> its rule puts on
    /// <paramref name="date"/>, planned as its goal's session (its <see cref="PlannedActivity.GoalId"/>, or itself when
    /// it has none): that occurrence is taken out of its way, its other sessions count as the goal's (one a day, and the
    /// spread, §7.3), the part of the day it was in is preferred and the time it has now isn't offered. Null when it
    /// doesn't happen that day.
    /// </summary>
    public static SessionAlternatives? OptionsFor(PlanRequest request, PlannedActivity activity, LocalDate date, int priority, int most)
    {
        if (ActivityCheck.OccurrenceOn(activity.Series, date) is not { } occurrence)
        {
            return null;
        }

        var goalId = activity.GoalId ?? activity.Id;
        var activities = request.Activities
            .Where(other => other.Id != activity.Id)
            .Append(activity with { Series = activity.Series.CancelOccurrence(date), GoalId = goalId })
            .ToList();
        var start = occurrence.Time.Start.InZone(request.Zone).TimeOfDay;
        var session = new PlanGoal(
            goalId,
            activity.Title,
            1,
            occurrence.Time.Duration,
            activity.Buffers,
            activity.LocationId,
            activity.IsDemanding,
            priority,
            new HashSet<IsoDayOfWeek>(),
            new[] { TimeOfDay.Morning, TimeOfDay.Midday, TimeOfDay.Afternoon, TimeOfDay.Evening }
                .FirstOrDefault(part => HoursOf(part) is { } hours && start >= hours.From && start < hours.To));
        return Options(request with { Activities = activities, Goals = [session] }, session, most, notAt: occurrence.Time.Start);
    }

    /// <summary>
    /// §7.3: what a time scores, in one place (documented in the scheduling-rules skill). Higher is better.
    /// </summary>
    internal static class Scores
    {
        /// <summary>On one of the goal's preferred days.</summary>
        public const int PreferredDay = 30;

        /// <summary>Starting in the goal's preferred time of day.</summary>
        public const int PreferredTime = 20;

        /// <summary>For each day from the nearest other session of the same goal, up to <see cref="MostDaysApart"/>.</summary>
        public const int PerDayApart = 10;

        public const int MostDaysApart = 3;

        /// <summary>Taken off for each half hour already taken up that day (daily load, lower is better).</summary>
        public const int PerHalfHourOfLoad = 1;

        /// <summary>For each quarter hour free before and after the footprint, up to an hour each side (slack).</summary>
        public const int PerQuarterHourOfSlack = 1;

        public static readonly Duration MostSlack = Duration.FromHours(1);

        /// <summary>For each day before the week's last: earlier leaves room to catch up.</summary>
        public const int PerDayEarlier = 1;
    }

    /// <summary>The start times each part of the day covers, as [from, to).</summary>
    public static (LocalTime From, LocalTime To)? HoursOf(TimeOfDay time) => time switch
    {
        TimeOfDay.Morning => (new LocalTime(5, 0), new LocalTime(12, 0)),
        TimeOfDay.Midday => (new LocalTime(11, 0), new LocalTime(14, 0)),
        TimeOfDay.Afternoon => (new LocalTime(12, 0), new LocalTime(17, 0)),
        TimeOfDay.Evening => (new LocalTime(17, 0), new LocalTime(22, 0)),
        _ => null,
    };

    private static string Sentence(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    // Which rule a start time breaks, in the order they are checked.
    private enum Broken
    {
        None,
        Past,
        GoalAlreadyThatDay,
        SaidNo,
        RecoveryDay,
        MostDemanding,
        Busy,
        OpeningHours,
        RestGap,
    }

    private readonly record struct Verdict(Broken Broken, Footprint Footprint, OpeningHoursProblem? Hours = null, Instant? Near = null);

    // Everything known about the week while it is planned: busy time, demanding sessions, each goal's sessions and
    // each day's load, with the suggestions added as they are made.
    private sealed class WeekState
    {
        private readonly PlanRequest _request;
        private readonly DateTimeZone _zone;
        private readonly List<Interval> _busy = [];
        private readonly List<Instant> _demanding = [];
        private readonly Dictionary<Guid, List<LocalDate>> _sessions = [];
        private readonly Dictionary<LocalDate, Duration> _load = [];
        private readonly SortedSet<string> _notes = new(StringComparer.Ordinal);

        public WeekState(PlanRequest request)
        {
            _request = request;
            _zone = request.Zone;
            // Wide enough for clashes and for the rest between demanding sessions (H5, scheduling review H-1).
            var loaded = DatesToLoadFor(request.Week, request.Rest.BetweenDemanding);
            foreach (var activity in request.Activities)
            {
                // Travel that can't be worked out is left out, as on the home page, and said (P1).
                var known = Travel.KnownJourneys(activity.LocationId, request.Context);
                var journeys = known ?? Journeys.None;
                var occurrences = activity.Series.OccurrencesBetween(loaded.Start, loaded.End);
                if (known is null && occurrences.Count > 0)
                {
                    _notes.Add($"Travel to and from {activity.Title} isn't counted, because {Travel.WhyUnknown(activity.LocationId, request.Context)}");
                }

                foreach (var occurrence in occurrences)
                {
                    var longest = Times.LongestFor(occurrence.Time, activity.Series.Zone);
                    var footprint = Footprint.ProblemsWith(occurrence.Time.Duration, activity.Buffers, journeys, longest).Count == 0
                        ? Footprint.Around(occurrence.Time, activity.Buffers, journeys, longest)
                        : null;
                    _busy.Add(footprint?.Whole ?? occurrence.Time);
                    AddLoad(footprint?.Whole ?? occurrence.Time);
                    if (activity.IsDemanding)
                    {
                        _demanding.Add(occurrence.Time.Start);
                    }

                    if (activity.GoalId is { } goal)
                    {
                        SessionsOf(goal).Add(occurrence.Time.Start.InZone(_zone).Date);
                    }
                }
            }

            for (var night = request.Week.Start.PlusDays(-1); night <= request.Week.End; night = night.PlusDays(1))
            {
                if (SleepOn(night) is { } sleep)
                {
                    _busy.Add(sleep);
                }
            }
        }

        public IReadOnlyList<LocalDate> SessionDates(Guid goal) => _sessions.TryGetValue(goal, out var dates) ? dates : [];

        /// <summary>SCH-6: a start not to offer, the time the session has now.</summary>
        public Instant? NotAt { get; init; }

        /// <summary>What couldn't be counted, in a fixed order (SCH-7).</summary>
        public IReadOnlyList<string> Notes => _notes.ToList();

        // SCH-5 (scheduling review M-2): why, when no day gave a reason: they are over, or each day left already has
        // as many of the goal's sessions as a day allows.
        public string NoDaysLeft(PlanGoal goal)
        {
            var lastStart = _request.Week.End.PlusDays(1).AtStartOfDayInZone(_zone).ToInstant();
            if (lastStart <= _request.Now)
            {
                return "No days are left this week";
            }

            return SessionsPerDay(goal) == 1
                ? $"Every day left this week already has a {goal.Title} session (one a day)"
                : $"Every day left this week already has two {goal.Title} sessions (two a day)";
        }

        public void Add(Suggestion suggestion)
        {
            _busy.Add(suggestion.Footprint.Whole);
            AddLoad(suggestion.Footprint.Whole);
            SessionsOf(suggestion.Goal.Id).Add(suggestion.Time.Start.InZone(_zone).Date);
            if (suggestion.Goal.IsDemanding)
            {
                _demanding.Add(suggestion.Time.Start);
            }
        }

        // The best-scoring start time that keeps every rule, the earliest on a tie; null when none does.
        public Suggestion? Best(PlanGoal goal, Journeys journeys)
        {
            (int Score, Verdict Verdict, LocalDate Date)? best = null;
            foreach (var date in _request.Week)
            {
                if (BestOn(goal, journeys, date) is { } onDate && (best is null || onDate.Score > best.Value.Score))
                {
                    best = onDate;
                }
            }

            return best is { } chosen ? SuggestionFor(goal, chosen.Verdict, chosen.Date) : null;
        }

        // SCH-6: the best time on each day that has one, best first (the earliest on a tie).
        public IEnumerable<Suggestion> BestEachDay(PlanGoal goal, Journeys journeys) =>
            _request.Week
                .Select(date => BestOn(goal, journeys, date))
                .OfType<(int Score, Verdict Verdict, LocalDate Date)>()
                .OrderByDescending(best => best.Score)
                .ThenBy(best => best.Verdict.Footprint.Activity.Start)
                .Select(best => SuggestionFor(goal, best.Verdict, best.Date));

        // The best-scoring start time on the date that keeps every rule, the earliest on a tie.
        private (int Score, Verdict Verdict, LocalDate Date)? BestOn(PlanGoal goal, Journeys journeys, LocalDate date)
        {
            (int Score, Verdict Verdict, LocalDate Date)? best = null;
            foreach (var start in StartsOn(date))
            {
                var verdict = Check(goal, journeys, date, start);
                if (verdict.Broken != Broken.None || start == NotAt)
                {
                    continue;
                }

                var score = Score(goal, date, verdict.Footprint).Score;
                if (best is null || score > best.Value.Score)
                {
                    best = (score, verdict, date);
                }
            }

            return best;
        }

        private Suggestion SuggestionFor(PlanGoal goal, Verdict verdict, LocalDate date) =>
            new(goal, verdict.Footprint.Activity, verdict.Footprint, Score(goal, date, verdict.Footprint).Reasons);

        // SCH-5: for each day not over and without a session of this goal, the rule that ruled it out, with numbers.
        public IReadOnlyList<DayReason> DayReasons(PlanGoal goal, Journeys journeys)
        {
            var reasons = new List<DayReason>();
            foreach (var date in _request.Week)
            {
                var verdicts = StartsOn(date).Select(start => Check(goal, journeys, date, start)).Where(verdict => verdict.Broken != Broken.Past).ToList();
                if (verdicts.Count == 0 || verdicts.Any(verdict => verdict.Broken is Broken.GoalAlreadyThatDay or Broken.None))
                {
                    continue;
                }

                reasons.Add(new DayReason(date, ReasonFor(goal, journeys, date, verdicts)));
            }

            return reasons;
        }

        // SCH-5's "a shorter session": the longest, in steps of 5 minutes, that would fit somewhere this week.
        public string? Shorter(PlanGoal goal, Journeys journeys, Duration shortest)
        {
            for (var length = goal.Length - Duration.FromMinutes(5); length >= shortest; length -= Duration.FromMinutes(5))
            {
                if (Best(goal with { Length = length }, journeys) is { } fits)
                {
                    var start = fits.Time.Start.InZone(_zone);
                    return $"A session of {Times.Length(length)} would fit: {Times.Date.Format(start.Date)} at {Times.Clock(start, _request.Context.Clock)}";
                }
            }

            return null;
        }

        private List<LocalDate> SessionsOf(Guid goal)
        {
            if (!_sessions.TryGetValue(goal, out var dates))
            {
                dates = [];
                _sessions[goal] = dates;
            }

            return dates;
        }

        // A footprint crossing midnight counts towards each day for its part on that day (§8, §7.3).
        private void AddLoad(Interval time)
        {
            foreach (var share in CalendarDays.Split(time, _zone))
            {
                _load[share.Day] = _load.GetValueOrDefault(share.Day) + share.Length;
            }
        }

        // The night after date: from winding down before bed to getting going after waking. A bedtime before midday is
        // after midnight, so on the next date.
        private Interval? SleepOn(LocalDate date)
        {
            var sleep = _request.Sleep;
            var bed = (sleep.Bedtime >= new LocalTime(12, 0) ? date : date.PlusDays(1)).At(sleep.Bedtime);
            var wake = bed.Date.At(sleep.WakeTime);
            if (wake <= bed)
            {
                wake = wake.PlusDays(1);
            }

            var from = ClockTimes.ToInstant(bed, _zone) - sleep.WindDown;
            var to = ClockTimes.ToInstant(wake, _zone) + sleep.WakeUpRoutine;
            return to > from ? new Interval(from, to) : null;
        }

        // Every quarter hour on the date that happens exactly once: on the nights the clocks change, the skipped and
        // repeated times are left out rather than guessed.
        private IEnumerable<Instant> StartsOn(LocalDate date)
        {
            for (var minutes = 0; minutes < 24 * 60; minutes += (int)Step.TotalMinutes)
            {
                var mapping = _zone.MapLocal(date.At(LocalTime.Midnight.PlusMinutes(minutes)));
                if (mapping.Count == 1)
                {
                    yield return mapping.Single().ToInstant();
                }
            }
        }

        private Verdict Check(PlanGoal goal, Journeys journeys, LocalDate date, Instant start)
        {
            var rest = _request.Rest;
            var footprint = Footprint.Around(new Interval(start, start + goal.Length), goal.Buffers, journeys, Footprint.LongestOn(date, _zone));
            if (footprint.Whole.Start < _request.Now)
            {
                return new Verdict(Broken.Past, footprint);
            }

            if (SessionDates(goal.Id).Count(day => day == date) >= SessionsPerDay(goal))
            {
                return new Verdict(Broken.GoalAlreadyThatDay, footprint);
            }

            if (_request.Rejected.Contains(new RejectedDay(goal.Id, date)))
            {
                return new Verdict(Broken.SaidNo, footprint);
            }

            if (goal.IsDemanding && rest.RecoveryDays.Contains(date.DayOfWeek))
            {
                return new Verdict(Broken.RecoveryDay, footprint);
            }

            if (goal.IsDemanding && _demanding.Count(other => other.InZone(_zone).Date == date) >= rest.MostDemandingPerDay)
            {
                return new Verdict(Broken.MostDemanding, footprint);
            }

            if (_busy.Any(busy => busy.Start < footprint.Whole.End && footprint.Whole.Start < busy.End))
            {
                return new Verdict(Broken.Busy, footprint);
            }

            if (goal.LocationId is { } at && _request.Context.Places.TryGetValue(at, out var place)
                && place.OpeningHours.ProblemWith(footprint.Activity, _zone) is { } closed)
            {
                return new Verdict(Broken.OpeningHours, footprint, Hours: closed);
            }

            if (goal.IsDemanding)
            {
                var near = _demanding
                    .Where(other => Abs(other - start) < rest.BetweenDemanding)
                    .OrderBy(other => Abs(other - start))
                    .ThenBy(other => other)
                    .Select(other => (Instant?)other)
                    .FirstOrDefault();
                if (near is not null)
                {
                    return new Verdict(Broken.RestGap, footprint, Near: near);
                }
            }

            return new Verdict(Broken.None, footprint);
        }

        private (int Score, IReadOnlyList<string> Reasons) Score(PlanGoal goal, LocalDate date, Footprint footprint)
        {
            var score = 0;
            var reasons = new List<string>();
            if (goal.PreferredDays.Contains(date.DayOfWeek))
            {
                score += Scores.PreferredDay;
                reasons.Add("Your preferred day");
            }

            if (HoursOf(goal.PreferredTime) is { } hours && footprint.Activity.Start.InZone(_zone).TimeOfDay is var time && time >= hours.From && time < hours.To)
            {
                score += Scores.PreferredTime;
                reasons.Add($"{PartOfDay(goal.PreferredTime)}, as you prefer");
            }

            var others = SessionDates(goal.Id);
            var apart = others.Count == 0 ? Scores.MostDaysApart : others.Min(other => Math.Abs(Period.Between(other, date, PeriodUnits.Days).Days));
            score += Math.Min(apart, Scores.MostDaysApart) * Scores.PerDayApart;
            if (others.Count > 0 && apart >= 2)
            {
                reasons.Add($"{apart} days from your nearest other {goal.Title}");
            }

            var load = _load.GetValueOrDefault(date);
            score -= (int)(load.TotalMinutes / 30) * Scores.PerHalfHourOfLoad;
            if (load == Duration.Zero)
            {
                reasons.Add("Nothing else planned that day");
            }
            else if (load < Duration.FromHours(2))
            {
                reasons.Add("A light day");
            }

            score += Slack(footprint);
            score += (6 - Period.Between(_request.Week.Start, date, PeriodUnits.Days).Days) * Scores.PerDayEarlier;
            return (score, reasons);
        }

        // Free time before and after the footprint, up to an hour each side, a point per quarter hour.
        private int Slack(Footprint footprint)
        {
            var whole = footprint.Whole;
            var before = _busy.Where(busy => busy.End <= whole.Start).Select(busy => whole.Start - busy.End).DefaultIfEmpty(Scores.MostSlack).Min();
            var after = _busy.Where(busy => busy.Start >= whole.End).Select(busy => busy.Start - whole.End).DefaultIfEmpty(Scores.MostSlack).Min();
            return QuarterHours(before) + QuarterHours(after);

            static int QuarterHours(Duration free) => (int)(Duration.Min(free, Scores.MostSlack).TotalMinutes / 15) * Scores.PerQuarterHourOfSlack;
        }

        private string ReasonFor(PlanGoal goal, Journeys journeys, LocalDate date, List<Verdict> verdicts)
        {
            var clock = _request.Context.Clock;
            var first = verdicts[0];
            switch (first.Broken)
            {
                case Broken.SaidNo:
                    return "You said no to this day";
                case Broken.RecoveryDay:
                    return $"A recovery day, and {goal.Title} is demanding";
                case Broken.MostDemanding:
                    var most = _request.Rest.MostDemandingPerDay;
                    return $"It already has {most} demanding {(most == 1 ? "session" : "sessions")}, the most for a day";
            }

            var free = verdicts.Where(verdict => verdict.Broken != Broken.Busy).ToList();
            if (free.Count == 0)
            {
                var whole = goal.Length + goal.Buffers.Preparation + goal.Buffers.Changing + journeys.Out + journeys.Back;
                var withAround = goal.Buffers == Buffers.None && journeys == Journeys.None ? "" : " with getting ready, travel and changing";
                if (LongestFreeOn(date) is not { } longest)
                {
                    return $"There's no free time; {goal.Title} needs {Times.Length(whole)}{withAround}";
                }

                // Long enough, but no quarter-hour start fits in it (scheduling review M-1): said, not left looking as
                // if it fitted.
                return longest.Duration < whole
                    ? $"The longest free time is {Times.Length(longest.Duration)}; {goal.Title} needs {Times.Length(whole)}{withAround}"
                    : $"The longest free time, {Times.Clock(longest.Start.InZone(_zone), clock)} to {Times.Clock(longest.End.InZone(_zone), clock)}, " +
                      $"is {Times.Length(longest.Duration)}, but with sessions starting on the quarter hour, {goal.Title} " +
                      $"({Times.Length(whole)}{withAround}) doesn't fit in it";
            }

            if (free.All(verdict => verdict.Broken == Broken.OpeningHours))
            {
                // That day's own hours: a time just after midnight can be in the day before's opening (review L-1).
                var name = Travel.NameOf(goal.LocationId!.Value, _request.Context);
                return _request.Context.Places[goal.LocationId.Value].OpeningHours.Days.TryGetValue(date.DayOfWeek, out var hours)
                    ? $"{name} is open {ClockText.Format(hours.Opens, clock)} to {ClockText.Format(hours.Closes, clock)}{(hours.Closes <= hours.Opens ? " the next day" : "")}, and your free time then is too short"
                    : $"{name} is closed that day";
            }

            var near = free.First(verdict => verdict.Broken == Broken.RestGap).Near!.Value.InZone(_zone);
            var apart = (int)_request.Rest.BetweenDemanding.TotalHours;
            return $"Too close to the demanding session at {Times.Clock(near, clock)} on {Times.Date.Format(near.Date)} " +
                   $"(demanding sessions are at least {apart} {(apart == 1 ? "hour" : "hours")} apart)";
        }

        // CAL-7: each day not over, its free time in all and its longest stretch.
        public IReadOnlyList<FreeDay> FreeTime() =>
            _request.Week
                .Where(date => date.PlusDays(1).AtStartOfDayInZone(_zone).ToInstant() > _request.Now)
                .Select(date => FreeOn(date) is var free && free.Count > 0
                    ? new FreeDay(date, free.Aggregate(Duration.Zero, (total, stretch) => total + stretch.Duration), free.Max(stretch => stretch.Duration))
                    : new FreeDay(date, Duration.Zero, Duration.Zero))
                .ToList();

        // The longest stretch of the date (not already over) with nothing planned and not asleep, or null for none; the
        // earliest of equal ones.
        private Interval? LongestFreeOn(LocalDate date) =>
            FreeOn(date).OrderByDescending(stretch => stretch.Duration).Select(stretch => (Interval?)stretch).FirstOrDefault();

        // The stretches of the date (from now, midnight to midnight) with nothing planned and not asleep, in time order.
        private List<Interval> FreeOn(LocalDate date)
        {
            var dayStart = Instant.Max(date.AtStartOfDayInZone(_zone).ToInstant(), _request.Now);
            var dayEnd = date.PlusDays(1).AtStartOfDayInZone(_zone).ToInstant();
            var free = new List<Interval>();
            var from = dayStart;
            foreach (var busy in _busy.Where(busy => busy.End > dayStart && busy.Start < dayEnd).OrderBy(busy => busy.Start))
            {
                if (busy.Start > from)
                {
                    free.Add(new Interval(from, busy.Start));
                }

                from = Instant.Max(from, busy.End);
            }

            if (dayEnd > from)
            {
                free.Add(new Interval(from, dayEnd));
            }

            return free;
        }

        private static Duration Abs(Duration duration) => duration < Duration.Zero ? -duration : duration;

        // One session of a goal a day, or two for one of more than 7 a week (at most 14).
        private static int SessionsPerDay(PlanGoal goal) => (goal.SessionsPerWeek + 6) / 7;

        private static string PartOfDay(TimeOfDay time) => time switch
        {
            TimeOfDay.Morning => "In the morning",
            TimeOfDay.Midday => "Around midday",
            TimeOfDay.Afternoon => "In the afternoon",
            _ => "In the evening",
        };
    }
}
