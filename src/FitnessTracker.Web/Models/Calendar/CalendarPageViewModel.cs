namespace FitnessTracker.Web.Models.Calendar;

/// <summary>
/// The calendar page (CAL-1): what FullCalendar needs from the user's preferences. <see cref="FirstDay"/> is
/// FullCalendar's numbering (Sunday 0 to Saturday 6); <see cref="Unreadable"/> names activities it can't show.
/// </summary>
public sealed record CalendarPageViewModel(string EventsUrl, string TimeZoneId, int FirstDay, bool Hour12, IReadOnlyList<string> Unreadable);
