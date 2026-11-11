using System.Globalization;

namespace FitnessTracker.Domain.Workouts;

/// <summary>ACC-3: the unit weights are shown and typed in.</summary>
public enum WeightUnit
{
    Kilograms,
    Pounds,
}

/// <summary>
/// Q33: weights are stored in whole grams (exact, as distances are whole metres: CLAUDE.md rule 6), and shown and typed
/// in the unit the user chose.
/// </summary>
public static class Weights
{
    /// <summary>An international avoirdupois pound.</summary>
    public const decimal GramsPerPound = 453.59237m;

    /// <summary>
    /// <paramref name="amount"/> in <paramref name="unit"/> to the nearest whole gram (a half rounds up): 102.5 kg is
    /// 102,500 g, 225 lb is 102,058 g. More grams than an int holds throws, rather than wrapping round.
    /// </summary>
    public static int Grams(decimal amount, WeightUnit unit) =>
        decimal.ToInt32(Math.Round(amount * GramsIn(unit), MidpointRounding.AwayFromZero));

    /// <summary><paramref name="grams"/> in <paramref name="unit"/>, not rounded: 454 g is 1.0009… lb.</summary>
    public static decimal InUnit(long grams, WeightUnit unit) => grams / GramsIn(unit);

    /// <summary>
    /// As pages show it: up to two decimals and the unit ("102.5 kg", "1,000 kg", "225 lb"). A long, for a workout's
    /// total weight lifted.
    /// </summary>
    public static string Text(long grams, WeightUnit unit)
    {
        var amount = Math.Round(InUnit(grams, unit), 2, MidpointRounding.AwayFromZero);
        return $"{amount.ToString("#,0.##", CultureInfo.InvariantCulture)} {(unit == WeightUnit.Kilograms ? "kg" : "lb")}";
    }

    /// <summary>
    /// For a form's weight box: the shortest number that gives back exactly <paramref name="grams"/> when saved
    /// unchanged, so opening and saving a set never moves it (100,000 g is "220.462" lb). Four decimals of a pound are
    /// within 0.05 g, so it never needs more.
    /// </summary>
    public static string InputText(int grams, WeightUnit unit)
    {
        var amount = InUnit(grams, unit);
        var decimals = 0;
        while (decimals < 4 && Grams(Math.Round(amount, decimals, MidpointRounding.AwayFromZero), unit) != grams)
        {
            decimals++;
        }

        return Math.Round(amount, decimals, MidpointRounding.AwayFromZero).ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static decimal GramsIn(WeightUnit unit) => unit == WeightUnit.Pounds ? GramsPerPound : 1000m;
}
