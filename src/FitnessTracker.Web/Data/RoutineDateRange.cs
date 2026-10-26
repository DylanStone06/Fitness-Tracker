using FitnessTracker.Domain.Templates;
using NodaTime;

namespace FitnessTracker.Web.Data;

/// <summary>
/// CAL-9, Q14: a routine chosen for a range of dates (a holiday week, a Brighton weekend), in use on them instead of the
/// usual routine. Ranges don't overlap: the pages refuse it (the domain still gives one answer if some do).
/// </summary>
public class RoutineDateRange : IOwnedByUser, IVersioned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string OwnerId { get; set; } = "";

    public Guid Version { get; set; }

    public Guid RoutineId { get; set; }

    /// <summary>The first date the routine is in use.</summary>
    public LocalDate StartDate { get; set; }

    /// <summary>The last date the routine is in use, inclusive.</summary>
    public LocalDate EndDate { get; set; }

    /// <summary>The domain's view of it.</summary>
    public RoutineDates ToRoutineDates() => new(RoutineId, new DateInterval(StartDate, EndDate));
}
