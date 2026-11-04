using System.Globalization;
using NodaTime;

namespace FitnessTracker.Domain.Workouts;

/// <summary>
/// FIT-3, Q27: a time as a stopwatch shows it, to the nearest second: "0:45", "28:36", and with hours once there is one,
/// "1:05:20". For a run's time and pace; a session's planned length is written as everywhere else ("1 h 30 min").
/// </summary>
public static class StopwatchText
{
    public static string Format(Duration time)
    {
        var seconds = (long)Math.Round(time.TotalSeconds, MidpointRounding.AwayFromZero);
        var (hours, minutes, andSeconds) = (seconds / 3600, seconds / 60 % 60, seconds % 60);
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{andSeconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{andSeconds:00}");
    }
}
