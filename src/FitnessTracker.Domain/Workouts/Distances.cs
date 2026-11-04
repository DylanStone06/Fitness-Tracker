using System.Globalization;

namespace FitnessTracker.Domain.Workouts;

/// <summary>ACC-3: the unit distances are shown and typed in.</summary>
public enum DistanceUnit
{
    Kilometres,
    Miles,
}

/// <summary>Q25: distances are stored in whole metres, and shown and typed in the unit the user chose.</summary>
public static class Distances
{
    /// <summary>An international mile.</summary>
    public const decimal MetresPerMile = 1609.344m;

    /// <summary>
    /// <paramref name="amount"/> in <paramref name="unit"/> to the nearest whole metre (a half rounds up): 5.2 km is
    /// 5200 m, 3.1 miles is 4989 m. More metres than an int holds throws, rather than wrapping round.
    /// </summary>
    public static int Metres(decimal amount, DistanceUnit unit) =>
        decimal.ToInt32(Math.Round(amount * MetresIn(unit), MidpointRounding.AwayFromZero));

    /// <summary><paramref name="metres"/> in <paramref name="unit"/>, not rounded: 1609 m is 0.99978… miles.</summary>
    public static decimal InUnit(int metres, DistanceUnit unit) => metres / MetresIn(unit);

    /// <summary>As pages show it: up to two decimals and the unit ("5.2 km", "1,000 km", "3.11 miles", "1 mile").</summary>
    public static string Text(int metres, DistanceUnit unit)
    {
        var amount = Math.Round(InUnit(metres, unit), 2, MidpointRounding.AwayFromZero);
        var number = amount.ToString("#,0.##", CultureInfo.InvariantCulture);
        return unit == DistanceUnit.Kilometres ? $"{number} km"
            : amount == 1 ? $"{number} mile"
            : $"{number} miles";
    }

    /// <summary>
    /// For a form's distance box: the shortest number that gives back exactly <paramref name="metres"/> when saved
    /// unchanged, so opening and saving a log never moves it (5000 m is "3.107" miles: "3.11" would be 5005 m). Four
    /// decimals of a mile are within 0.1 m, so it never needs more.
    /// </summary>
    public static string InputText(int metres, DistanceUnit unit)
    {
        var amount = InUnit(metres, unit);
        var decimals = 0;
        while (decimals < 4 && Metres(Math.Round(amount, decimals, MidpointRounding.AwayFromZero), unit) != metres)
        {
            decimals++;
        }

        return Math.Round(amount, decimals, MidpointRounding.AwayFromZero).ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static decimal MetresIn(DistanceUnit unit) => unit == DistanceUnit.Miles ? MetresPerMile : 1000m;
}
