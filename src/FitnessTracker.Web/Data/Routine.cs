using FitnessTracker.Domain.Templates;
using NodaTime;

namespace FitnessTracker.Web.Data;

/// <summary>
/// CAL-9: a routine template, a kind of week ("Usual week", "Holiday", "Brighton weekend") that repeats over 1 to 4
/// weeks: A/B weeks are a routine of 2. Repeating activities can be in every week of it or in one week of its cycle
/// (week A only). The usual routine is in use on every date that no other routine is chosen for.
/// </summary>
public class Routine : IOwnedByUser, IVersioned
{
    public const int NameMaxLength = 100;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string OwnerId { get; set; } = "";

    public Guid Version { get; set; }

    public string Name { get; set; } = "";

    /// <summary>How many weeks before it repeats, from 1 to <see cref="WeekCycle.MaxWeeks"/>.</summary>
    public int CycleWeeks { get; set; } = 1;

    /// <summary>
    /// The first day of a week A (requirements Q3: by default, the week it was added). The cycle counts weeks by the
    /// calendar from here, forwards and backwards.
    /// </summary>
    public LocalDate WeekAStarts { get; set; }

    /// <summary>
    /// The day its weeks start on: the week-start preference when it was added. Kept with the routine, as a repeat
    /// rule keeps its own (scheduling-rules skill), so changing that preference later doesn't move days into other
    /// weeks of the cycle.
    /// </summary>
    public IsoDayOfWeek WeekStart { get; set; } = IsoDayOfWeek.Monday;

    /// <summary>In use on every date no other routine is chosen for. At most one per person.</summary>
    public bool IsUsual { get; set; }

    /// <summary>The domain's view of its weeks.</summary>
    public WeekCycle ToCycle() => new(CycleWeeks, WeekAStarts, WeekStart);
}
