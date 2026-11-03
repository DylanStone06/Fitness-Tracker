using NodaTime;

namespace FitnessTracker.Domain.Workouts;

/// <summary>
/// FIT-3, Q25: how long each km or mile of a run took, on average. Worked out from its time and distance each time it
/// is shown, never stored, so it can't disagree with them.
/// </summary>
public static class Pace
{
    /// <summary>
    /// The time for each km or mile, to the nearest second, or null without both a time and a distance. Rounded here,
    /// before it is written, so 5:59.7 is "6:00", never "5:60".
    /// </summary>
    public static Duration? Per(Duration time, int metres, DistanceUnit unit)
    {
        if (time <= Duration.Zero || metres <= 0)
        {
            return null;
        }

        var seconds = (decimal)time.TotalSeconds / Distances.InUnit(metres, unit);
        return Duration.FromSeconds(decimal.ToInt64(Math.Round(seconds, MidpointRounding.AwayFromZero)));
    }

    /// <summary>"5:30 per km", "8:51 per mile", or null without both a time and a distance.</summary>
    public static string? Text(Duration time, int metres, DistanceUnit unit) =>
        Per(time, metres, unit) is { } pace
            ? $"{StopwatchText.Format(pace)} per {(unit == DistanceUnit.Kilometres ? "km" : "mile")}"
            : null;
}
