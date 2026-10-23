using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Domain.Time;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models.Activities;
using NodaTime;
using NodaTime.Text;

namespace FitnessTracker.Web.Models.Calendar;

/// <summary>One part of a footprint around the activity (CAL-5): getting ready, travel or changing, as clock times.</summary>
public sealed record CalendarSegment(string Part, string Start, string End);

/// <summary>
/// What the calendar shows about an occurrence besides its times: FullCalendar's "extendedProps". An occurrence of a
/// repeating activity (<see cref="Repeats"/>) opens its own page, to change just that one (CAL-4); <see cref="Moved"/>
/// says it was moved or resized on its own. <see cref="Minutes"/> is its real, elapsed length (§8), which a drag keeps.
/// <see cref="Starts"/> and <see cref="Ends"/> ("22:00", "06:00 the next day") are what the calendar writes, rather than
/// its own times, which would end a night shift's first part at midnight; <see cref="EndsAt"/> is the end without the day,
/// for that next day's part.
/// </summary>
public sealed record CalendarEventDetails(
    Guid ActivityId,
    string OccurrenceDate,
    bool Repeats,
    bool Moved,
    string? Category,
    string? Place,
    string Starts,
    string Ends,
    string EndsAt,
    int Minutes,
    string? Trip,
    string? Note,
    IReadOnlyList<string> ClashesWith,
    IReadOnlyList<CalendarSegment> Footprint);

/// <summary>
/// CAL-1: one occurrence as a FullCalendar event. Times are clock times in the user's timezone with no offset, which
/// FullCalendar shows as given; <see cref="CalendarEventDetails.Starts"/> says which 01:30 on the night the clocks go back.
/// </summary>
public sealed record CalendarEvent(string Id, string Title, string Start, string End, IReadOnlyList<string> ClassNames, CalendarEventDetails ExtendedProps)
{
    private static readonly LocalDateTimePattern Clock = LocalDateTimePattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd'T'HH':'mm':'ss");

    public static CalendarEvent From(AgendaItem item, DateTimeZone zone, ActivityCategory? category, string? place, bool repeats, ClockFormat clock)
    {
        // Clock times, drawn as given. On the night the clocks go back an end can read earlier than its start (01:45 BST
        // for 30 minutes ends 01:15 GMT), which the calendar can't draw: it is drawn as long as it really is instead,
        // and "starts" and "trip" say the real times (§8).
        (string Start, string End) Span(Interval time)
        {
            var start = time.Start.InZone(zone).LocalDateTime;
            var end = time.End.InZone(zone).LocalDateTime;
            return (Clock.Format(start), Clock.Format(end > start ? end : start.PlusTicks(time.Duration.BclCompatibleTicks)));
        }

        var occurrence = item.Occurrence;
        var classNames = new List<string> { "ft-event", ActivityDisplay.CategoryStyle(category ?? ActivityCategory.Other) };
        if (item.ClashesWith.Count > 0)
        {
            classNames.Add("ft-clash");
        }

        var segments = item.Footprint?.Segments
            .Where(segment => segment.Part != Domain.Footprints.FootprintPart.Activity)
            .Select(segment =>
            {
                var (start, end) = Span(segment.Time);
                return new CalendarSegment(segment.Part.ToString(), start, end);
            })
            .ToList() ?? [];

        var (eventStart, eventEnd) = Span(occurrence.Time);
        return new CalendarEvent(
            $"{item.Activity.Id:N}-{occurrence.Date:uuuu-MM-dd}",
            item.Activity.Title,
            eventStart,
            eventEnd,
            classNames,
            new CalendarEventDetails(
                item.Activity.Id,
                LocalDatePattern.Iso.Format(occurrence.Date),
                repeats,
                item.Activity.Series.Changes.ContainsKey(occurrence.Date),
                category is { } named ? ActivityDisplay.CategoryName(named) : null,
                place,
                Agenda.Starts(item, clock),
                Agenda.Ends(item, clock),
                Agenda.Ends(item, clock, sayTheDay: false),
                (int)occurrence.Time.Duration.TotalMinutes,
                Agenda.Trip(item, clock),
                item.Note,
                item.ClashesWith,
                segments));
    }
}
