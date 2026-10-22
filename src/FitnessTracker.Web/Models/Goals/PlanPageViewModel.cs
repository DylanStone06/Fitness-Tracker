using System.Globalization;
using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Domain.Time;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models.Activities;
using FitnessTracker.Web.Services;
using Microsoft.AspNetCore.Http.Extensions;
using NodaTime;
using NodaTime.Text;

namespace FitnessTracker.Web.Models.Goals;

/// <summary>
/// SCH-3, SCH-4: one suggested session. <see cref="EveryWeek"/> and <see cref="JustThisWeek"/> are the activity form's
/// fields, filled in from the goal, for adding it through the ordinary check on save (SCH-1, Q12). The queries open the
/// form to change it first, or plan again without this goal on this day ("Not this day").
/// </summary>
public sealed record SuggestionView(
    string Title,
    string CategoryName,
    string CategoryStyle,
    string Day,
    string Time,
    string? Place,
    string? Trip,
    IReadOnlyList<string> Reasons,
    IReadOnlyDictionary<string, string> EveryWeek,
    IReadOnlyDictionary<string, string> JustThisWeek,
    string ChangeQuery,
    string NoQuery);

/// <summary>SCH-5: sessions of a goal that don't fit, each day's reason, and what would.</summary>
public sealed record ShortfallView(string Title, string Summary, IReadOnlyList<string> Days, string? Shorter, string? Blocked);

/// <summary>How far a goal is planned already this week: "1 of 3 already planned".</summary>
public sealed record GoalProgress(string Title, string Planned);

/// <summary>The "Plan my week" page (SCH-3 to SCH-5).</summary>
public sealed class PlanPageViewModel
{
    private static readonly LocalDatePattern DayPattern = LocalDatePattern.CreateWithInvariantCulture("ddd d MMM");

    private static readonly LocalDatePattern FullDate = LocalDatePattern.CreateWithInvariantCulture("d MMM uuuu");

    private static readonly LocalDatePattern SkipDate = LocalDatePattern.CreateWithInvariantCulture("uuuuMMdd");

    public required string WeekFrom { get; init; }

    public required string WeekTo { get; init; }

    /// <summary>The week's first day ("2026-11-02"), and those of the weeks either side, for links.</summary>
    public required string Date { get; init; }

    public required string PreviousWeek { get; init; }

    public required string NextWeek { get; init; }

    public required bool IsThisWeek { get; init; }

    public required bool HasGoals { get; init; }

    public required IReadOnlyList<GoalProgress> Progress { get; init; }

    public required IReadOnlyList<SuggestionView> Suggestions { get; init; }

    public required IReadOnlyList<ShortfallView> Shortfalls { get; init; }

    public required IReadOnlyList<string> Unreadable { get; init; }

    /// <summary>SCH-4: a day said no to for a goal, as the page's address carries it: "{goal id}-20261104".</summary>
    public static string SkipFor(Guid goal, LocalDate date) => $"{goal:N}-{SkipDate.Format(date)}";

    /// <summary>Null for text the page didn't write.</summary>
    public static RejectedDay? ParseSkip(string? text)
    {
        var parts = (text ?? "").Split('-');
        return parts.Length == 2 && Guid.TryParseExact(parts[0], "N", out var goal) && SkipDate.Parse(parts[1]) is { Success: true } date
            ? new RejectedDay(goal, date.Value)
            : null;
    }

    public static PlanPageViewModel From(PlannedWeek planned, IReadOnlyList<string> skips)
    {
        var week = planned.Week;
        var zone = planned.User.Now.Zone;
        var clock = planned.User.Context.Clock;
        var places = planned.User.Locations.ToDictionary(location => location.Id, location => location.Name);
        var date = FormInput.Date.Format(week.Start);

        SuggestionView View(Suggestion suggestion)
        {
            var goal = planned.Goals[suggestion.Goal.Id];
            var start = suggestion.Time.Start.InZone(zone);
            var end = suggestion.Time.End.InZone(zone);
            var item = new AgendaItem(
                new PlannedActivity(goal.Id, goal.Title,
                    new Series(start.Date, start.TimeOfDay, suggestion.Goal.Length, zone, RecurrenceRule.Daily().EndingAfter(1)),
                    suggestion.Goal.Buffers, goal.LocationId),
                new Occurrence(start.Date, suggestion.Time),
                suggestion.Footprint,
                null,
                []);
            var no = new QueryBuilder { { "date", date } };
            foreach (var skip in skips.Append(SkipFor(goal.Id, start.Date)))
            {
                no.Add("skip", skip);
            }

            var change = new QueryBuilder
            {
                { "goal", goal.Id.ToString() },
                { "date", FormInput.Date.Format(start.Date) },
                { "time", FormInput.Time.Format(start.TimeOfDay) },
                { "everyWeek", "true" },
                { "returnToPlan", "true" },
            };
            return new SuggestionView(
                goal.Title,
                ActivityDisplay.CategoryName(goal.Category),
                ActivityDisplay.CategoryStyle(goal.Category),
                DayPattern.Format(start.Date),
                $"{Agenda.Starts(item, clock)} – {Agenda.Ends(item, clock)}",
                goal.LocationId is { } at && places.TryGetValue(at, out var place) ? place : null,
                Agenda.Trip(item, clock),
                suggestion.Reasons,
                FormFields(goal, start.LocalDateTime, everyWeek: true),
                FormFields(goal, start.LocalDateTime, everyWeek: false),
                change.ToString(),
                no.ToString());
        }

        var sameMonth = week.Start.Month == week.End.Month && week.Start.Year == week.End.Year;
        var sameYear = week.Start.Year == week.End.Year;
        return new PlanPageViewModel
        {
            WeekFrom = sameMonth ? week.Start.Day.ToString(CultureInfo.InvariantCulture)
                : sameYear ? LocalDatePattern.CreateWithInvariantCulture("d MMM").Format(week.Start)
                : FullDate.Format(week.Start),
            WeekTo = FullDate.Format(week.End),
            Date = date,
            PreviousWeek = FormInput.Date.Format(week.Start.PlusWeeks(-1)),
            NextWeek = FormInput.Date.Format(week.Start.PlusWeeks(1)),
            IsThisWeek = week.Contains(planned.User.Now.Date),
            HasGoals = planned.Goals.Count > 0,
            Progress = planned.Goals.Values
                .Where(goal => planned.Plan.AlreadyPlanned.GetValueOrDefault(goal.Id) > 0)
                .OrderByDescending(goal => goal.Priority)
                .ThenBy(goal => goal.Title, StringComparer.InvariantCultureIgnoreCase)
                .Select(goal => new GoalProgress(goal.Title, $"{Math.Min(planned.Plan.AlreadyPlanned[goal.Id], goal.SessionsPerWeek)} of {goal.SessionsPerWeek} already planned"))
                .ToList(),
            Suggestions = planned.Plan.Suggestions.Select(View).ToList(),
            Shortfalls = planned.Plan.Shortfalls.Select(shortfall => new ShortfallView(
                shortfall.Goal.Title,
                $"{shortfall.Missing} of {shortfall.Goal.SessionsPerWeek} {(shortfall.Goal.SessionsPerWeek == 1 ? "session doesn't" : "sessions don't")} fit this week.",
                shortfall.Note is { } note
                    ? [$"{note}."]
                    : shortfall.Days.Select(day => $"{DayPattern.Format(day.Date)}: {day.Reason}.").ToList(),
                shortfall.Shorter is { } shorter ? $"{shorter}." : null,
                shortfall.Blocked is { } blocked ? $"{blocked}." : null)).ToList(),
            // P1: what couldn't be read or counted, said rather than planned around as if it were known.
            Unreadable = [.. planned.Unreadable, .. planned.Plan.Notes.Select(note => $"{note}.")],
        };
    }

    // The activity form's fields for a session of the goal starting then: movable, as the planner chose its time, and
    // weekly on that day, from then on, or just once (Q12). Getting ready and changing are the goal's.
    private static Dictionary<string, string> FormFields(WeeklyGoal goal, LocalDateTime start, bool everyWeek)
    {
        var fields = new Dictionary<string, string>
        {
            ["Title"] = goal.Title,
            ["Category"] = goal.Category.ToString(),
            ["LocationId"] = goal.LocationId?.ToString() ?? "",
            ["StartDate"] = FormInput.Date.Format(start.Date),
            ["StartTime"] = FormInput.Time.Format(start.TimeOfDay),
            ["DurationHours"] = (goal.SessionMinutes / 60).ToString(CultureInfo.InvariantCulture),
            ["DurationMinutes"] = (goal.SessionMinutes % 60).ToString(CultureInfo.InvariantCulture),
            ["IsFixed"] = "false",
            ["Priority"] = goal.Priority.ToString(),
            ["IsDemanding"] = goal.IsDemanding ? "true" : "false",
            ["PreparationMinutes"] = goal.PreparationMinutes.ToString(CultureInfo.InvariantCulture),
            ["ChangingMinutes"] = goal.ChangingMinutes.ToString(CultureInfo.InvariantCulture),
            ["Repeats"] = everyWeek ? nameof(RepeatChoice.Weekly) : nameof(RepeatChoice.Never),
            ["RepeatEvery"] = "1",
            ["Ends"] = nameof(RepeatEndChoice.Never),
            ["GoalId"] = goal.Id.ToString(),
            ["ReturnToPlan"] = "true",
        };
        for (var i = 0; i < Week.Days.Count; i++)
        {
            fields[$"RepeatDays[{i}]"] = everyWeek && Week.Days[i] == start.DayOfWeek ? "true" : "false";
        }

        return fields;
    }
}
