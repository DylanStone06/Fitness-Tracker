using NodaTime;
using NodaTime.Text;

namespace FitnessTracker.Web.Models;

/// <summary>Dates and times as the forms' &lt;input type="date"&gt; and &lt;input type="time"&gt; send them.</summary>
public static class FormInput
{
    public const string OutsidePlannableYears = "Enter a date between the years 1900 and 2999.";

    public static readonly LocalDatePattern Date = LocalDatePattern.Iso;

    public static readonly LocalTimePattern Time = LocalTimePattern.CreateWithInvariantCulture("HH:mm");

    // Years the app plans in. Anything outside is almost certainly a mistyped year, and keeps date maths well away from
    // the end of the calendar (31 December 9999).
    private const int FirstPlannableYear = 1900;

    private const int LastPlannableYear = 2999;

    public static bool IsPlannable(LocalDate date) => date.Year is >= FirstPlannableYear and <= LastPlannableYear;

    public static LocalDate? ParseDate(string? text) =>
        text is not null && Date.Parse(text.Trim()) is { Success: true } result ? result.Value : null;

    public static LocalTime? ParseTime(string? text) =>
        text is not null && Time.Parse(text.Trim()) is { Success: true } result ? result.Value : null;
}
