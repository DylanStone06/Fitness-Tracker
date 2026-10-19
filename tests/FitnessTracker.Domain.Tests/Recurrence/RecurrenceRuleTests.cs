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

    // FallsOn checks one date. Monday, Wednesday and Friday from Monday 5 October 2026.
    [Theory]
    [InlineData(5, true)]
    [InlineData(6, false)] // a Tuesday
    [InlineData(12, true)] // a later week, checked without walking from the first date
    public void FallsOn_OnlyTheRulesDays(int octoberDay, bool expected)
    {
        var rule = RecurrenceRule.Weekly([IsoDayOfWeek.Monday, IsoDayOfWeek.Wednesday, IsoDayOfWeek.Friday]);

        Assert.Equal(expected, rule.FallsOn(firstDate: new(2026, 10, 5), date: new(2026, 10, octoberDay)));
    }

    [Fact]
    public void FallsOn_IsFalse_BeforeTheFirstDate_AfterTheCount_AndAfterTheEndDate()
    {
        var days = new[] { IsoDayOfWeek.Monday, IsoDayOfWeek.Wednesday, IsoDayOfWeek.Friday };
        var firstDate = new LocalDate(2026, 10, 5);

        Assert.False(RecurrenceRule.Weekly(days).FallsOn(firstDate, date: new(2026, 9, 28)));
        Assert.False(RecurrenceRule.Weekly(days).EndingAfter(3).FallsOn(firstDate, date: new(2026, 10, 12))); // 5, 7 and 9 were the three
        Assert.False(RecurrenceRule.Weekly(days).EndingOn(new(2026, 10, 9)).FallsOn(firstDate, date: new(2026, 10, 12)));
    }

    [Fact]
    public void EndingAfterZeroOccurrences_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RecurrenceRule.Daily().EndingAfter(0));
    }

    // Every date in the app is in the ordinary (ISO) calendar. Julian 31 December 2026 is ISO 13 January 2027, and
    // would be stored as "UNTIL=20261231", which reads back 13 days early.
    [Fact]
    public void AnEndDateInAnotherCalendar_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => RecurrenceRule.Daily().EndingOn(new LocalDate(2026, 12, 31, CalendarSystem.Julian)));
    }
}
