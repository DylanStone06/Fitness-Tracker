using FitnessTracker.Domain.Templates;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models.Routines;
using NodaTime;

namespace FitnessTracker.Web.Tests.Routines;

// SCH-4, Q12, Q15 with CAL-9: on Plan my week, "every week" adds a session to the weeks of the routine in use on its
// date, and the button says which. A two-week usual routine (week A the week of 19 October 2026), a holiday with its
// own two-week cycle chosen for 9-22 November, and a weekend away that is the same every week, chosen for 7-8 November.
public class RoutineDisplayTests
{
    private static readonly Guid Usual = Guid.NewGuid();
    private static readonly Guid Holiday = Guid.NewGuid();
    private static readonly Guid Weekend = Guid.NewGuid();

    private static readonly Routine[] Routines =
    [
        new() { Id = Usual, Name = "Usual week", CycleWeeks = 2, IsUsual = true, WeekAStarts = new(2026, 10, 19) },
        new() { Id = Holiday, Name = "Holiday", CycleWeeks = 2, WeekAStarts = new(2026, 10, 19) },
        new() { Id = Weekend, Name = "Weekend away", CycleWeeks = 1, WeekAStarts = new(2026, 10, 19) },
    ];

    private static readonly RoutineCalendar Calendar = new(
        Routines.ToDictionary(routine => routine.Id, routine => routine.ToCycle()),
        Usual,
        [
            new RoutineDates(Holiday, new DateInterval(new(2026, 11, 9), new(2026, 11, 22))),
            new RoutineDates(Weekend, new DateInterval(new(2026, 11, 7), new(2026, 11, 8))),
        ]);

    // Planning week B of the usual routine builds week B (Q15).
    [Fact]
    public void InTheUsualRoutine_ItIsThatWeekOfIt()
    {
        Assert.Equal(($"{Usual}/B", "Every week B"), RoutineDisplay.EveryWeekFor(new LocalDate(2026, 10, 26), Calendar, Routines));
    }

    [Fact]
    public void InARoutineThatIsTheSameEveryWeek_ItIsWheneverThatRoutineIsInUse()
    {
        Assert.Equal((Weekend.ToString(), "Whenever Weekend away is in use"), RoutineDisplay.EveryWeekFor(new LocalDate(2026, 11, 7), Calendar, Routines));
    }

    // A holiday with its own cycle: that week of the holiday, said so (scheduling review, Phase 6b: it said only
    // "Every week B", as for the usual routine).
    [Fact]
    public void InAnotherRoutineWithACycle_ItIsThatWeekOfItWheneverItIsInUse()
    {
        Assert.Equal(($"{Holiday}/B", "Week B whenever Holiday is in use"), RoutineDisplay.EveryWeekFor(new LocalDate(2026, 11, 9), Calendar, Routines));
    }

    [Fact]
    public void WithoutRoutines_ItIsEveryWeek()
    {
        Assert.Equal(("", "Every week"), RoutineDisplay.EveryWeekFor(new LocalDate(2026, 11, 9), RoutineCalendar.None, []));
    }
}
