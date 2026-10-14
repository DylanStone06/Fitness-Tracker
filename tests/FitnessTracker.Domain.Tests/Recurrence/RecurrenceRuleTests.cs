using FitnessTracker.Domain.Recurrence;
using NodaTime;

namespace FitnessTracker.Domain.Tests.Recurrence;

// CAL-3: rules that could never produce a sensible series are refused when they are made, so the dates can
// always be worked out (a weekly rule with no days would otherwise search forever).
public class RecurrenceRuleTests
{
    [Fact]
    public void RepeatingEveryZeroDaysOrWeeks_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RecurrenceRule.Daily(everyDays: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RecurrenceRule.Weekly([IsoDayOfWeek.Monday], everyWeeks: 0));
    }

    [Fact]
    public void WeeklyWithNoDays_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => RecurrenceRule.Weekly([]));
    }

    // NodaTime's IsoDayOfWeek includes None (0), which is not a day.
    [Fact]
    public void WeeklyWithADayThatIsNotADay_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RecurrenceRule.Weekly([IsoDayOfWeek.None]));
        Assert.Throws<ArgumentOutOfRangeException>(() => RecurrenceRule.Weekly([IsoDayOfWeek.Monday], weekStart: IsoDayOfWeek.None));
    }

    // A rule may be shared, so its days can't be changed after it is made, not even by casting.
    [Fact]
    public void ARulesDays_CannotBeChangedAfterwards()
    {
        var rule = RecurrenceRule.Weekly([IsoDayOfWeek.Monday]);

        Assert.Throws<NotSupportedException>(() => ((ICollection<IsoDayOfWeek>)rule.Days).Add(IsoDayOfWeek.Saturday));
    }

    [Fact]
    public void EndingAfterZeroOccurrences_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RecurrenceRule.Daily().EndingAfter(0));
    }
}
