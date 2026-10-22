using NodaTime;
using NodaTime.Text;

namespace FitnessTracker.Web.Models;

/// <summary>Dates and times as the forms' &lt;input type="date"&gt; and &lt;input type="time"&gt; send them.</summary>
public static class FormInput
{
    public const string OutsidePlannableYears = "Enter a date between the years 1900 and 2999.";

    public static readonly LocalDatePattern Date = LocalDatePattern.Iso;

    public static readonly LocalTimePattern Time = LocalTimePattern.CreateWithInvariantCulture("HH:mm");

    // As the calendar sends a dragged time.
    private static readonly LocalDateTimePattern DraggedStart = LocalDateTimePattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd'T'HH':'mm");

    // Years the app plans in. Anything outside is almost certainly a mistyped year, and keeps date maths well away from
    // the end of the calendar (31 December 9999).
    private const int FirstPlannableYear = 1900;

    private const int LastPlannableYear = 2999;

    public static bool IsPlannable(LocalDate date) => date.Year is >= FirstPlannableYear and <= LastPlannableYear;

    public static LocalDate? ParseDate(string? text) =>
        text is not null && Date.Parse(text.Trim()) is { Success: true } result ? result.Value : null;

    public static LocalTime? ParseTime(string? text) =>
        text is not null && Time.Parse(text.Trim()) is { Success: true } result ? result.Value : null;

    /// <summary>
    /// CAL-8: a time dragged to on the calendar, as it sends it ("2026-11-05T18:30" for 45 minutes), or null when it
    /// can't be read or is outside what a form accepts (a link edited by hand).
    /// </summary>
    public static (LocalDateTime Start, int Minutes)? Dragged(string? start, int? minutes) =>
        start is not null && DraggedStart.Parse(start) is { Success: true } parsed && IsPlannable(parsed.Value.Date)
            && minutes is >= 1 and <= 25 * 60
            ? (parsed.Value, minutes.Value)
            : null;
}
