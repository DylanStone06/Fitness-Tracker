using FitnessTracker.Domain.Footprints;
using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Services;
using NodaTime;
using NodaTime.Text;

namespace FitnessTracker.Web.Models.Activities;

/// <summary>SCH-6: another time for the session: when, what it takes up, why, and the page that moves it there.</summary>
public sealed record OtherTimeView(string Day, string Time, string? Trip, IReadOnlyList<string> Reasons, string MoveUrl);

/// <summary>
/// SCH-6, T16: other times later in its week for one occurrence of an activity (a missed or skipped one, or one to
/// move), with nothing else moved. Each opens the occurrence's page, or a one-off's form, with that time filled in
/// (CAL-8's pages), so the move is checked and confirmed there (SCH-1).
/// </summary>
public sealed class RescheduleViewModel
{
    private static readonly LocalDatePattern DayPattern = LocalDatePattern.CreateWithInvariantCulture("ddd d MMM");

    private static readonly LocalDateTimePattern Start = LocalDateTimePattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd'T'HH':'mm");

    public required Activity Activity { get; init; }

    /// <summary>The occurrence's own date as the page carries it ("2026-11-02").</summary>
    public required string Date { get; init; }

    public required bool Repeats { get; init; }

    /// <summary>When it is now: "Mon 2 Nov at 18:00 – 19:00".</summary>
    public required string Now { get; init; }

    public required IReadOnlyList<OtherTimeView> Options { get; init; }

    /// <summary>Why there are none: each day's reason, or one sentence.</summary>
    public required IReadOnlyList<string> NoneBecause { get; init; }

    public required IReadOnlyList<string> Notes { get; init; }

    /// <summary>Opened from the calendar: going back, or the move once saved, returns to it.</summary>
    public bool ReturnToCalendar { get; set; }

    /// <param name="moveUrl">The page that moves it to a start ("2026-11-04T18:30") for a number of minutes.</param>
    public static RescheduleViewModel From(Activity activity, OtherTimes found, Func<string, int, string> moveUrl)
    {
        var zone = found.User.Now.Zone;
        var clock = found.User.Context.Clock;
        var minutes = (int)found.Occurrence.Time.Duration.TotalMinutes;

        AgendaItem Item(Interval time, Footprint? footprint)
        {
            var start = time.Start.InZone(zone);
            var planned = new PlannedActivity(activity.Id, activity.Title,
                new Series(start.Date, start.TimeOfDay, time.Duration, zone, RecurrenceRule.Daily().EndingAfter(1)),
                new Buffers(Duration.FromMinutes(activity.PreparationMinutes), Duration.FromMinutes(activity.ChangingMinutes)),
                activity.LocationId);
            return new AgendaItem(planned, new Occurrence(start.Date, time), footprint, null, [])
            {
                LeavesFrom = Agenda.LeavesFrom(found.User.Context, start.Date),
            };
        }

        string When(Interval time)
        {
            var item = Item(time, null);
            return $"{DayPattern.Format(time.Start.InZone(zone).Date)}, {Agenda.Starts(item, clock)} – {Agenda.Ends(item, clock)}";
        }

        var none = found.Options.None;
        return new RescheduleViewModel
        {
            Activity = activity,
            Date = FormInput.Date.Format(found.Occurrence.Date),
            Repeats = activity.RepeatRule is not null,
            Now = When(found.Occurrence.Time),
            Options = found.Options.Options.Select(option =>
            {
                var item = Item(option.Time, option.Footprint);
                return new OtherTimeView(
                    DayPattern.Format(option.Time.Start.InZone(zone).Date),
                    $"{Agenda.Starts(item, clock)} – {Agenda.Ends(item, clock)}",
                    Agenda.Trip(item, clock),
                    option.Reasons,
                    moveUrl(Start.Format(option.Time.Start.InZone(zone).LocalDateTime), minutes));
            }).ToList(),
            NoneBecause = none is null ? []
                : none.Blocked is { } blocked ? [$"{blocked}."]
                : none.Note is { } note ? [$"{note}."]
                : none.Days.Select(day => $"{DayPattern.Format(day.Date)}: {day.Reason}.").ToList(),
            Notes = found.Unreadable,
        };
    }
}
