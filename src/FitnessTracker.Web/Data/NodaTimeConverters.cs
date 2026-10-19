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
