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
/// says it was moved or resized on its own.
/// </summary>
public sealed record CalendarEventDetails(
    Guid ActivityId,
    string OccurrenceDate,
    bool Repeats,
    bool Moved,
    string? Category,
    string? Place,
    string Starts,
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
        string At(Instant moment) => Clock.Format(moment.InZone(zone).LocalDateTime);

        var occurrence = item.Occurrence;
        var classNames = new List<string> { "ft-event", ActivityDisplay.CategoryStyle(category ?? ActivityCategory.Other) };
        if (item.ClashesWith.Count > 0)
        {
            classNames.Add("ft-clash");
        }

        var segments = item.Footprint?.Segments
            .Where(segment => segment.Part != Domain.Footprints.FootprintPart.Activity)
            .Select(segment => new CalendarSegment(segment.Part.ToString(), At(segment.Time.Start), At(segment.Time.End)))
            .ToList() ?? [];

        return new CalendarEvent(
            $"{item.Activity.Id:N}-{occurrence.Date:uuuu-MM-dd}",
            item.Activity.Title,
            At(occurrence.Time.Start),
            At(occurrence.Time.End),
            classNames,
            new CalendarEventDetails(
                item.Activity.Id,
                LocalDatePattern.Iso.Format(occurrence.Date),
                repeats,
                item.Activity.Series.Changes.ContainsKey(occurrence.Date),
                category is { } named ? ActivityDisplay.CategoryName(named) : null,
                place,
                Agenda.Starts(item, clock),
                Agenda.Trip(item, clock),
                item.Note,
                item.ClashesWith,
                segments));
    }
}
