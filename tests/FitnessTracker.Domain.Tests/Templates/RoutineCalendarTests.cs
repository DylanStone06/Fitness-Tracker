using FitnessTracker.Domain.Templates;
using NodaTime;

namespace FitnessTracker.Domain.Tests.Templates;

// CAL-9: which routine is in use on each date, and which week of its cycle. The usual routine is in use on every date
// that no other routine has been chosen for; a routine chosen for some dates (a holiday week, a Brighton weekend) wins
// there. Monday 5 October 2026 starts a week.
public class RoutineCalendarTests
{
    private static readonly Guid Usual = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Holiday = new("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid Brighton = new("00000000-0000-0000-0000-00000000000c");
    private static readonly LocalDate Monday5th = new(2026, 10, 5);

    private static readonly Dictionary<Guid, WeekCycle> Cycles = new()
    {
        [Usual] = new WeekCycle(weeks: 2, anchor: Monday5th),
        [Holiday] = new WeekCycle(weeks: 1, anchor: Monday5th),
        [Brighton] = new WeekCycle(weeks: 1, anchor: Monday5th),
    };

    [Fact]
    public void WithNoRoutines_NoDateIsInOne()
    {
        Assert.Null(RoutineCalendar.None.On(Monday5th));
    }

    [Fact]
    public void TheUsualRoutine_IsInUseOnEveryDate_InTheWeekOfItsCycle() // T18, with a routine
    {
        var calendar = new RoutineCalendar(Cycles, Usual, []);

        Assert.Equal(new RoutineWeek(Usual, 'A'), calendar.On(Monday5th));
        Assert.Equal(new RoutineWeek(Usual, 'B'), calendar.On(new LocalDate(2026, 10, 26)));
        Assert.Equal(new RoutineWeek(Usual, 'B'), calendar.On(new LocalDate(2026, 9, 28)));
    }

    [Fact]
    public void ARoutineChosenForSomeDates_IsInUseOnThem_AndTheUsualOneAfterwards()
    {
        var calendar = new RoutineCalendar(Cycles, Usual, [new RoutineDates(Holiday, new DateInterval(new(2026, 10, 12), new(2026, 10, 18)))]);

        Assert.Equal(new RoutineWeek(Usual, 'A'), calendar.On(new LocalDate(2026, 10, 11)));
        Assert.Equal(new RoutineWeek(Holiday, 'A'), calendar.On(new LocalDate(2026, 10, 12)));
        Assert.Equal(new RoutineWeek(Holiday, 'A'), calendar.On(new LocalDate(2026, 10, 18)));
        Assert.Equal(new RoutineWeek(Usual, 'A'), calendar.On(new LocalDate(2026, 10, 19)));
    }

    // A rota doesn't pause for a holiday: the week after a holiday week B is week A, as it would have been.
    [Fact]
    public void TheUsualCycle_KeepsCountingThroughDatesUsedByAnotherRoutine()
    {
        var calendar = new RoutineCalendar(Cycles, Usual, [new RoutineDates(Holiday, new DateInterval(new(2026, 10, 12), new(2026, 10, 18)))]);

        Assert.Equal(new RoutineWeek(Usual, 'A'), calendar.On(new LocalDate(2026, 10, 19)));
        Assert.Equal(new RoutineWeek(Usual, 'B'), calendar.On(new LocalDate(2026, 10, 26)));
    }

    // A Brighton weekend: Friday to Sunday, in the middle of a usual week.
    [Fact]
    public void ARoutineCanBeChosenForPartOfAWeek()
    {
        var calendar = new RoutineCalendar(Cycles, Usual, [new RoutineDates(Brighton, new DateInterval(new(2026, 10, 9), new(2026, 10, 11)))]);

        Assert.Equal(new RoutineWeek(Usual, 'A'), calendar.On(new LocalDate(2026, 10, 8)));
        Assert.Equal(new RoutineWeek(Brighton, 'A'), calendar.On(new LocalDate(2026, 10, 9)));
        Assert.Equal(new RoutineWeek(Usual, 'B'), calendar.On(new LocalDate(2026, 10, 12)));
    }

    [Fact]
    public void WithoutAUsualRoutine_OnlyTheChosenDatesAreInOne()
    {
        var calendar = new RoutineCalendar(Cycles, usual: null, [new RoutineDates(Holiday, new DateInterval(new(2026, 10, 12), new(2026, 10, 18)))]);

        Assert.Null(calendar.On(new LocalDate(2026, 10, 11)));
        Assert.Equal(new RoutineWeek(Holiday, 'A'), calendar.On(new LocalDate(2026, 10, 12)));
    }

    // The pages refuse dates that overlap; if any did (edited by hand), the answer is still the same every time (SCH-7):
    // the dates chosen to start later win, as the more specific choice.
    [Fact]
    public void OverlappingChosenDates_TheLaterStartWins()
    {
        var calendar = new RoutineCalendar(Cycles, Usual,
        [
            new RoutineDates(Brighton, new DateInterval(new(2026, 10, 16), new(2026, 10, 18))),
            new RoutineDates(Holiday, new DateInterval(new(2026, 10, 12), new(2026, 10, 18))),
        ]);

        Assert.Equal(new RoutineWeek(Holiday, 'A'), calendar.On(new LocalDate(2026, 10, 15)));
        Assert.Equal(new RoutineWeek(Brighton, 'A'), calendar.On(new LocalDate(2026, 10, 16)));
    }

    // A routine that has gone (deleted in another tab while a page was reading) is skipped rather than stopping the page.
    [Fact]
    public void DatesOrAUsualRoutineWithNoCycle_AreLeftOut()
    {
        var gone = Guid.NewGuid();
        var calendar = new RoutineCalendar(Cycles, usual: gone, [new RoutineDates(gone, new DateInterval(new(2026, 10, 12), new(2026, 10, 18)))]);

        Assert.Null(calendar.On(new LocalDate(2026, 10, 14)));
    }

    [Fact]
    public void AnActivityInEveryWeekOfARoutine_HappensWheneverThatRoutineIsInUse()
    {
        var calendar = new RoutineCalendar(Cycles, Usual, [new RoutineDates(Holiday, new DateInterval(new(2026, 10, 12), new(2026, 10, 18)))]);

        Assert.True(calendar.Includes(Usual, week: null, Monday5th));
        Assert.False(calendar.Includes(Usual, week: null, new LocalDate(2026, 10, 12)));
        Assert.True(calendar.Includes(Holiday, week: null, new LocalDate(2026, 10, 12)));
        Assert.True(calendar.Includes(Usual, week: null, new LocalDate(2026, 10, 26)));
    }

    [Fact]
    public void AnActivityInOneWeekOfARoutine_HappensOnlyInThatWeekOfItsCycle()
    {
        var calendar = new RoutineCalendar(Cycles, Usual, []);

        Assert.True(calendar.Includes(Usual, 'A', Monday5th));
        Assert.False(calendar.Includes(Usual, 'B', Monday5th));
        Assert.True(calendar.Includes(Usual, 'B', new LocalDate(2026, 10, 12)));
        Assert.False(calendar.Includes(Usual, 'A', new LocalDate(2026, 10, 18)));
    }

    // Week C of a two-week cycle never comes round (the pages don't offer it).
    [Fact]
    public void AWeekBeyondTheCycle_NeverComesRound()
    {
        var calendar = new RoutineCalendar(Cycles, Usual, []);

        Assert.DoesNotContain(Enumerable.Range(0, 28), day => calendar.Includes(Usual, 'C', Monday5th.PlusDays(day)));
    }
}
