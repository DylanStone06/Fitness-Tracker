using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Domain.Time;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models.Calendar;
using FitnessTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;

namespace FitnessTracker.Web.Controllers;

// CAL-1: the weekly calendar. Events is its data: the signed-in user's own occurrences only, through the per-user
// filter (T19, CalendarTests).
public class CalendarController(AppDbContext db, PlanningData planning) : Controller
{
    /// <summary>The most days one request may ask for: a month view shows six weeks.</summary>
    public const int MostDays = 42;

    /// <summary>
    /// The occurrences on screen from <paramref name="start"/> up to (not including) <paramref name="end"/>, clock
    /// times in the user's timezone as FullCalendar sends them ("2026-10-19T00:00:00", or a date).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Events(string? start, string? end)
    {
        if (ClockTime(start) is not { } from || ClockTime(end) is not { } to)
        {
            return BadRequest(new { error = "Give start and end as dates, for example 2026-10-19." });
        }

        if (to <= from || Period.Between(from.Date, to.Date, PeriodUnits.Days).Days > MostDays)
        {
            return BadRequest(new { error = $"The end must be after the start, and at most {MostDays} days later." });
        }

        var user = await planning.LoadAsync();
        var zone = user.Now.Zone;
        var window = new Interval(ClockTimes.ToInstant(from, zone), ClockTimes.ToInstant(to, zone));
        var activities = await planning.ActivitiesOnAsync(Agenda.DatesToLoadFor(window, zone), []);
        var items = Agenda.Overlapping(activities, user.Context, window, zone);

        var categories = await db.Activities
            .Select(activity => new { activity.Id, activity.Category })
            .ToDictionaryAsync(activity => activity.Id, activity => activity.Category);
        var places = user.Locations.ToDictionary(location => location.Id, location => location.Name);
        return Json(items.Select(item => CalendarEvent.From(
            item,
            zone,
            categories.TryGetValue(item.Activity.Id, out var category) ? category : null,
            item.Activity.LocationId is { } at && places.TryGetValue(at, out var place) ? place : null)));
    }

    private static LocalDateTime? ClockTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var moment = LocalDateTimePattern.ExtendedIso.Parse(text);
        if (moment.Success)
        {
            return moment.Value;
        }

        var date = LocalDatePattern.Iso.Parse(text);
        return date.Success ? date.Value.AtMidnight() : null;
    }
}
