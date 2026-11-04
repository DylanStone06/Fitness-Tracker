using NodaTime.Text;

namespace FitnessTracker.Domain.Time;

/// <summary>Dates as the explanations and pages write them: "Mon 2 Nov 2026", the same everywhere.</summary>
public static class DateText
{
    public static readonly LocalDatePattern Named = LocalDatePattern.CreateWithInvariantCulture("ddd d MMM uuuu");
}
