using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NodaTime;
using NodaTime.Text;

namespace FitnessTracker.Web.Data;

/// <summary>
/// Stores a NodaTime clock time (a bedtime, a series' start time) as text such as "23:00:00", which reads naturally
/// in the database and sorts correctly. Seconds are kept; anything finer is not needed by the app.
/// </summary>
public sealed class LocalTimeConverter() : ValueConverter<LocalTime, string>(
    time => Pattern.Format(time),
    text => Pattern.Parse(text).Value)
{
    private static readonly LocalTimePattern Pattern = LocalTimePattern.CreateWithInvariantCulture("HH:mm:ss");
}

/// <summary>Stores a NodaTime date (an activity's start date) as text such as "2026-10-19", which sorts correctly.</summary>
public sealed class LocalDateConverter() : ValueConverter<LocalDate, string>(
    date => Pattern.Format(date),
    text => Pattern.Parse(text).Value)
{
    private static readonly LocalDatePattern Pattern = LocalDatePattern.Iso;
}
