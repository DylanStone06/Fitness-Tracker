using System.Globalization;
using FitnessTracker.Domain.Workouts;

namespace FitnessTracker.Domain.Tests.Workouts;

// Q25: distances are stored in whole metres (CLAUDE.md rule 6), and shown and typed in km or miles, as chosen in
// Preferences. A mile is 1609.344 metres.
public class DistancesTests
{
    [Theory]
    [InlineData("5.2", DistanceUnit.Kilometres, 5200)]
    [InlineData("42.195", DistanceUnit.Kilometres, 42195)]
    [InlineData("1", DistanceUnit.Miles, 1609)] // 1609.344
    [InlineData("3.1", DistanceUnit.Miles, 4989)] // 4988.97
    [InlineData("26.2", DistanceUnit.Miles, 42165)] // 42164.81
    [InlineData("0.0005", DistanceUnit.Kilometres, 1)] // half a metre rounds up
    public void AnAmount_IsStoredAsTheNearestWholeMetre(string amount, DistanceUnit unit, int metres)
    {
        Assert.Equal(metres, Distances.Metres(decimal.Parse(amount, CultureInfo.InvariantCulture), unit));
    }

    // Far more than anyone runs, and more metres than the stored number holds: refused, never wrapped round. The page
    // only accepts up to 1000.
    [Fact]
    public void AnAmountTooLargeToStore_IsRefused()
    {
        Assert.Throws<OverflowException>(() => Distances.Metres(10_000_000m, DistanceUnit.Miles));
    }

    [Theory]
    [InlineData(5000, DistanceUnit.Kilometres, "5")]
    [InlineData(1609, DistanceUnit.Miles, "0.99978")]
    public void Metres_InAUnit_AreNotRounded(int metres, DistanceUnit unit, string startsWith)
    {
        Assert.StartsWith(startsWith, Distances.InUnit(metres, unit).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(5200, DistanceUnit.Kilometres, "5.2 km")]
    [InlineData(10000, DistanceUnit.Kilometres, "10 km")]
    [InlineData(400, DistanceUnit.Kilometres, "0.4 km")]
    [InlineData(42195, DistanceUnit.Kilometres, "42.2 km")] // 42.195: up to two decimals, the half rounding up
    [InlineData(1000000, DistanceUnit.Kilometres, "1,000 km")]
    [InlineData(4989, DistanceUnit.Miles, "3.1 miles")]
    [InlineData(5000, DistanceUnit.Miles, "3.11 miles")]
    [InlineData(1609, DistanceUnit.Miles, "1 mile")] // 0.9998: shown as 1, so one mile
    [InlineData(42195, DistanceUnit.Miles, "26.22 miles")]
    public void Text_HasUpToTwoDecimals_AndTheUnit(int metres, DistanceUnit unit, string text)
    {
        Assert.Equal(text, Distances.Text(metres, unit));
    }

    // A form box filled with a saved distance gives back exactly the same metres when saved unchanged: 3.11 miles would
    // be 5005 m, so 5000 m is "3.107".
    [Theory]
    [InlineData(5000, DistanceUnit.Miles, "3.107")]
    [InlineData(4989, DistanceUnit.Miles, "3.1")]
    [InlineData(5200, DistanceUnit.Kilometres, "5.2")]
    [InlineData(5000, DistanceUnit.Kilometres, "5")]
    [InlineData(1, DistanceUnit.Kilometres, "0.001")]
    public void InputText_IsTheShortestNumberGivingBackTheSameMetres(int metres, DistanceUnit unit, string text)
    {
        Assert.Equal(text, Distances.InputText(metres, unit));
    }

    // ... for every distance up to 200 km, in both units.
    [Theory]
    [InlineData(DistanceUnit.Kilometres)]
    [InlineData(DistanceUnit.Miles)]
    public void InputText_AlwaysGivesBackTheSameMetres(DistanceUnit unit)
    {
        var changed = Enumerable.Range(1, 200_000)
            .Where(metres => Distances.Metres(decimal.Parse(Distances.InputText(metres, unit), CultureInfo.InvariantCulture), unit) != metres)
            .Take(5)
            .ToList();

        Assert.Empty(changed);
    }
}
