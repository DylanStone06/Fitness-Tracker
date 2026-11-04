using FitnessTracker.Domain.Templates;
using NodaTime;

namespace FitnessTracker.Domain.Tests.Templates;

// CAL-9 and T18: a routine template can cycle over 1-4 weeks (A/B weeks). Week A is the week of the anchor date
// (by default the week the template is first applied, requirements Q3). Monday 5 October 2026 starts a week.
public class WeekCycleTests
{
    private static readonly LocalDate Monday5th = new(2026, 10, 5);

    [Fact]
    public void ATwoWeekCycle_IsInWeekB_ThreeWeeksAfterWeekA() // T18
    {
        var cycle = new WeekCycle(weeks: 2, anchor: Monday5th);

        Assert.Equal('B', cycle.WeekOf(new LocalDate(2026, 10, 26)));
    }

    [Theory]
    [InlineData(5, 'A')]
    [InlineData(11, 'A')] // Sunday, still the first week
    [InlineData(12, 'B')]
    [InlineData(19, 'C')]
    [InlineData(26, 'A')]
    public void AThreeWeekCycle_GoesRoundAgain(int octoberDay, char expected)
    {
        var cycle = new WeekCycle(weeks: 3, anchor: Monday5th);

        Assert.Equal(expected, cycle.WeekOf(new LocalDate(2026, 10, octoberDay)));
    }

    // Weeks before the anchor continue the same pattern backwards.
    [Theory]
    [InlineData(2026, 9, 28, 'B')]
    [InlineData(2026, 9, 21, 'A')]
    [InlineData(2026, 10, 4, 'B')] // the Sunday just before week A
    public void BeforeTheAnchor_TheCycleRunsBackwards(int year, int month, int day, char expected)
    {
        var cycle = new WeekCycle(weeks: 2, anchor: Monday5th);

        Assert.Equal(expected, cycle.WeekOf(new LocalDate(year, month, day)));
    }

    [Fact]
    public void AnAnchorMidWeek_MakesItsWholeWeekWeekA()
    {
        var cycle = new WeekCycle(weeks: 2, anchor: new LocalDate(2026, 10, 7));

        Assert.Equal('A', cycle.WeekOf(Monday5th));
        Assert.Equal('B', cycle.WeekOf(new LocalDate(2026, 10, 12)));
    }

    // With weeks starting on Sunday, Sunday 11 October begins week B.
    [Fact]
    public void WeeksFollowTheWeekStartDay()
    {
        var cycle = new WeekCycle(weeks: 2, anchor: new LocalDate(2026, 10, 4), weekStart: IsoDayOfWeek.Sunday);

        Assert.Equal('A', cycle.WeekOf(new LocalDate(2026, 10, 10)));
        Assert.Equal('B', cycle.WeekOf(new LocalDate(2026, 10, 11)));
    }

    [Fact]
    public void AFourWeekCycle_TheLongestAllowed_ReachesWeekD()
    {
        var cycle = new WeekCycle(weeks: 4, anchor: Monday5th);

        Assert.Equal('D', cycle.WeekOf(new LocalDate(2026, 10, 26)));
        Assert.Equal('A', cycle.WeekOf(new LocalDate(2026, 11, 2)));
    }

    [Fact]
    public void AOneWeekCycle_IsAlwaysWeekA()
    {
        var cycle = new WeekCycle(weeks: 1, anchor: Monday5th);

        Assert.Equal('A', cycle.WeekOf(new LocalDate(2026, 12, 30)));
        Assert.Equal('A', cycle.WeekOf(new LocalDate(2026, 1, 1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void ACycleOutsideOneToFourWeeks_IsRefused(int weeks)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WeekCycle(weeks, Monday5th));
    }

    [Fact]
    public void AWeekStartThatIsNotADay_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WeekCycle(2, Monday5th, IsoDayOfWeek.None));
    }
}
