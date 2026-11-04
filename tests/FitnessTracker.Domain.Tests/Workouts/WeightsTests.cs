using System.Globalization;
using FitnessTracker.Domain.Workouts;

namespace FitnessTracker.Domain.Tests.Workouts;

// Q33: weights are stored in whole grams (exact, as distances are whole metres: CLAUDE.md rule 6), and shown and typed
// in kg or lb as chosen in Preferences. A pound is 453.59237 grams.
public class WeightsTests
{
    [Theory]
    [InlineData("100", WeightUnit.Kilograms, 100_000)]
    [InlineData("102.5", WeightUnit.Kilograms, 102_500)]
    [InlineData("0.0005", WeightUnit.Kilograms, 1)] // half a gram rounds up
    [InlineData("1", WeightUnit.Pounds, 454)] // 453.59
    [InlineData("225", WeightUnit.Pounds, 102_058)] // 102,058.28
    [InlineData("2.5", WeightUnit.Pounds, 1_134)] // 1,133.98
    public void AnAmount_IsStoredAsTheNearestWholeGram(string amount, WeightUnit unit, int grams)
    {
        Assert.Equal(grams, Weights.Grams(decimal.Parse(amount, CultureInfo.InvariantCulture), unit));
    }

    // More grams than the stored number holds: refused, never wrapped round. The page only accepts up to 1,000 kg.
    [Fact]
    public void AnAmountTooLargeToStore_IsRefused()
    {
        Assert.Throws<OverflowException>(() => Weights.Grams(10_000_000m, WeightUnit.Pounds));
    }

    [Theory]
    [InlineData(100_000, WeightUnit.Kilograms, "100")]
    [InlineData(454, WeightUnit.Pounds, "1.0008")]
    public void Grams_InAUnit_AreNotRounded(int grams, WeightUnit unit, string startsWith)
    {
        Assert.StartsWith(startsWith, Weights.InUnit(grams, unit).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(100_000, WeightUnit.Kilograms, "100 kg")]
    [InlineData(102_500, WeightUnit.Kilograms, "102.5 kg")]
    [InlineData(1_250, WeightUnit.Kilograms, "1.25 kg")]
    [InlineData(1_000_000, WeightUnit.Kilograms, "1,000 kg")]
    [InlineData(102_058, WeightUnit.Pounds, "225 lb")] // 224.9994: up to two decimals
    [InlineData(1_134, WeightUnit.Pounds, "2.5 lb")]
    [InlineData(454, WeightUnit.Pounds, "1 lb")]
    public void Text_HasUpToTwoDecimals_AndTheUnit(int grams, WeightUnit unit, string text)
    {
        Assert.Equal(text, Weights.Text(grams, unit));
    }

    // A form box filled with a saved weight gives back exactly the same grams when saved unchanged: 225 lb is 102,058 g,
    // and "225" saves back to it.
    [Theory]
    [InlineData(102_058, WeightUnit.Pounds, "225")]
    [InlineData(100_000, WeightUnit.Pounds, "220.462")] // 220.46 would be 99,999 g
    [InlineData(102_500, WeightUnit.Kilograms, "102.5")]
    [InlineData(1, WeightUnit.Kilograms, "0.001")]
    public void InputText_IsTheShortestNumberGivingBackTheSameGrams(int grams, WeightUnit unit, string text)
    {
        Assert.Equal(text, Weights.InputText(grams, unit));
    }

    // ... for every weight up to 400 kg in steps of 7 grams, in both units.
    [Theory]
    [InlineData(WeightUnit.Kilograms)]
    [InlineData(WeightUnit.Pounds)]
    public void InputText_AlwaysGivesBackTheSameGrams(WeightUnit unit)
    {
        var changed = Enumerable.Range(0, 400_000 / 7).Select(step => 1 + (step * 7))
            .Where(grams => Weights.Grams(decimal.Parse(Weights.InputText(grams, unit), CultureInfo.InvariantCulture), unit) != grams)
            .Take(5)
            .ToList();

        Assert.Empty(changed);
    }
}
